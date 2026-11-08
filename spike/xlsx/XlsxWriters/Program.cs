using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using XlsxWriters;

var argsList = args.ToList();
if (argsList.Count == 0)
{
    PrintUsage();
    return 1;
}

var command = argsList[0].ToLowerInvariant();
var options = ParseOptions(argsList.Skip(1));
var outRoot = Path.GetFullPath(options.GetValueOrDefault("out", "spike/xlsx/out"));
Directory.CreateDirectory(outRoot);

return command switch
{
    "generate" => Generate(options, outRoot),
    "bench" => Bench(options, outRoot),
    "writer" => RunSingleWriter(options, outRoot),
    "verify" => Verify(options, outRoot),
    _ => Unknown(command)
};

static int Unknown(string command)
{
    Console.Error.WriteLine($"Неизвестная команда: {command}");
    PrintUsage();
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("""
        Использование:
          generate --rows <N> [--out <dir>]                   создать stage-CSV
          bench     --rows <N> --writer all|<name> [--reader all|tf|fast] [--repeats <K>]
          verify    --rows <N> --writer all|<name>
          writer    --rows <N> --writer <name> --reader <tf|fast>   (служебная, запускается bench)

        Писатели: closedxml | miniexcel | openxml | all
        """);
}

static Dictionary<string, string> ParseOptions(IEnumerable<string> source)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    string? key = null;
    foreach (var arg in source)
    {
        if (arg.StartsWith("--", StringComparison.Ordinal))
        {
            if (key is not null) result[key] = "true";
            key = arg[2..];
        }
        else if (key is not null)
        {
            result[key] = arg;
            key = null;
        }
    }
    if (key is not null) result[key] = "true";
    return result;
}

static int Generate(Dictionary<string, string> options, string outRoot)
{
    var rows = int.Parse(options.GetValueOrDefault("rows", "10000"), CultureInfo.InvariantCulture);
    var path = Path.Combine(outRoot, $"stage-{rows}.csv");
    var stopwatch = Stopwatch.StartNew();
    StageGenerator.Generate(path, rows);
    stopwatch.Stop();
    Console.WriteLine($"stage: {path}");
    Console.WriteLine($"rows={rows} size={new FileInfo(path).Length:N0} по; время={stopwatch.ElapsedMilliseconds} мс");
    return 0;
}

static int Bench(Dictionary<string, string> options, string outRoot)
{
    var rows = int.Parse(options.GetValueOrDefault("rows", "10000"), CultureInfo.InvariantCulture);
    var repeats = int.Parse(options.GetValueOrDefault("repeats", "3"), CultureInfo.InvariantCulture);
    var writers = ResolveWriters(options.GetValueOrDefault("writer", "all"));
    var readers = options.GetValueOrDefault("reader", "all");
    var stage = EnsureStage(outRoot, rows);

    foreach (var writer in writers)
    foreach (var reader in ResolveReaders(readers))
    {
        var samples = new List<BenchSample>();
        for (var run = 1; run <= repeats; run++)
        {
            var sample = RunWriterProcess(writer, reader, rows, stage, outRoot, run);
            if (sample is null)
            {
                Console.WriteLine($"FAIL {writer}/{reader} (см. output выше)");
                return 1;
            }
            samples.Add(sample);
            Console.WriteLine($"{writer}/{reader} run{run}: time={sample.TotalMs} мс, peak={sample.PeakWorkingSetMb:F0} МБ, alloc={sample.AllocatedMb:F0} МБ, file={sample.FileSizeBytes / 1024.0 / 1024.0:F1} МБ");
        }

        var median = Median(samples);
        Console.WriteLine($"MEDIAN {writer}/{reader}: time={median.TotalMs} мс, peak={median.PeakWorkingSetMb:F0} МБ, alloc={median.AllocatedMb:F0} МБ, file={median.FileSizeBytes / 1024.0 / 1024.0:F1} МБ");
    }

    return 0;
}

static int RunSingleWriter(Dictionary<string, string> options, string outRoot)
{
    var rows = int.Parse(options.GetValueOrDefault("rows", "10000"), CultureInfo.InvariantCulture);
    var writerName = options.GetValueOrDefault("writer", "closedxml");
    var readerName = options.GetValueOrDefault("reader", "tf");
    var stage = EnsureStage(outRoot, rows);

    var writer = CreateWriter(writerName);
    var reader = CreateReader(readerName);
    var target = Path.Combine(outRoot, $"out-{writer.Name.ToLowerInvariant()}-{reader.Name}-{rows}.xlsx");
    if (File.Exists(target)) File.Delete(target);

    var process = Process.GetCurrentProcess();
    GC.Collect();
    GC.WaitForPendingFinalizers();
    var allocations = GC.GetTotalAllocatedBytes(precise: true);
    var stopwatch = Stopwatch.StartNew();
    writer.Write(reader, stage, StageSchema.Columns, target);
    stopwatch.Stop();
    var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocations;
    process.Refresh();

    var sample = new BenchSample(
        writer.Name,
        reader.Name,
        rows,
        stopwatch.ElapsedMilliseconds,
        process.PeakWorkingSet64,
        allocated,
        new FileInfo(target).Length);
    Console.WriteLine(JsonSerializer.Serialize(sample));
    return 0;
}

static int Verify(Dictionary<string, string> options, string outRoot)
{
    var rows = int.Parse(options.GetValueOrDefault("rows", "10000"), CultureInfo.InvariantCulture);
    var writers = ResolveWriters(options.GetValueOrDefault("writer", "all"));
    var failures = 0;

    foreach (var writerName in writers)
    {
        var writer = CreateWriter(writerName);
        var target = Path.Combine(outRoot, $"out-{writer.Name.ToLowerInvariant()}-TextFieldParser-{rows}.xlsx");
        if (!File.Exists(target))
        {
            Console.WriteLine($"SKIP {writer.Name}: файл {target} не найден (сначала bench)");
            continue;
        }

        try
        {
            var report = Verifier.Verify(target, rows);
            Console.WriteLine($"OK {writer.Name}: rows={report.Rows}, cols={report.Columns}" +
                              (report.SchemaErrors is null ? "" : $", schema-errors={report.SchemaErrors}"));
            foreach (var check in report.Checks) Console.WriteLine($"   {check}");
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"FAIL {writer.Name}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    return failures == 0 ? 0 : 1;
}

static BenchSample? RunWriterProcess(string writer, string reader, int rows, string stage, string outRoot, int run)
{
    var arguments = new List<string>
    {
        "run", "-c", "Release", "--project", "spike/xlsx/XlsxWriters", "--",
        "writer", "--rows", rows.ToString(CultureInfo.InvariantCulture),
        "--writer", writer, "--reader", reader, "--out", outRoot,
        "--run", run.ToString(CultureInfo.InvariantCulture)
    };

    var startInfo = new ProcessStartInfo("dotnet", arguments)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    using var process = Process.Start(startInfo)!;
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        Console.Error.WriteLine(stderr);
        return null;
    }

    var line = stdout.Split('\n').Select(line => line.Trim()).LastOrDefault(line => line.StartsWith('{'));
    return line is null ? null : JsonSerializer.Deserialize<BenchSample>(line);
}

static BenchSample Median(List<BenchSample> samples)
{
    var ordered = samples.OrderBy(sample => sample.TotalMs).ToArray();
    return ordered[ordered.Length / 2];
}

static string EnsureStage(string outRoot, int rows)
{
    var path = Path.Combine(outRoot, $"stage-{rows}.csv");
    if (!File.Exists(path))
    {
        Console.WriteLine($"Генерирую stage для {rows} строк...");
        StageGenerator.Generate(path, rows);
    }
    return path;
}

static IXlsxWriter CreateWriter(string name) => name.ToLowerInvariant() switch
{
    "closedxml" => new ClosedXmlWriter(),
    "miniexcel" => new MiniExcelWriter(),
    "openxml" => new OpenXmlStreamWriter(),
    _ => throw new ArgumentException($"Неизвестный писатель: {name}")
};

static IStageReader CreateReader(string name) => name.ToLowerInvariant() switch
{
    "tf" or "textfieldparser" => new TextFieldParserStageReader(),
    "fast" => new FastStageReader(),
    _ => throw new ArgumentException($"Неизвестный читатель: {name}")
};

static string[] ResolveWriters(string name) => name.ToLowerInvariant() switch
{
    "all" => ["closedxml", "miniexcel", "openxml"],
    _ => [name]
};

static string[] ResolveReaders(string name) => name.ToLowerInvariant() switch
{
    "all" => ["tf", "fast"],
    _ => [name]
};

internal sealed record BenchSample(string Writer, string Reader, int Rows, long TotalMs, long PeakWorkingSet, long AllocatedBytes, long FileSizeBytes)
{
    public double PeakWorkingSetMb => PeakWorkingSet / 1024.0 / 1024.0;
    public double AllocatedMb => AllocatedBytes / 1024.0 / 1024.0;
}