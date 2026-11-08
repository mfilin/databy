using System.Diagnostics;
using System.Globalization;
using System.Text;
using Databy.Reports.Input;
using Xunit.Abstractions;

namespace Tests.Unit;

[Trait("Category", "T1")]
public sealed class InputScaleTests(ITestOutputHelper output)
{
    [Fact]
    public void Is01_HundredThousandRowsAreStreamed()
    {
        Run(100_000, TimeSpan.FromSeconds(60), output);
    }

    internal static void Run(int rows, TimeSpan limit, ITestOutputHelper? output = null)
    {
        var path = Path.Combine(Path.GetTempPath(), "databy-input-scale-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
                for (var i = 1; i <= rows; i++)
                    writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{i}\t2026-01-15\tзначение {i}"));

            GC.Collect();
            GC.WaitForPendingFinalizers();
            var before = GC.GetTotalAllocatedBytes(true);
            var processBefore = Process.GetCurrentProcess().PeakWorkingSet64;
            var stopwatch = Stopwatch.StartNew();

            using var result = new ListInputParser().ParseFile(path, new InputParseOptions
            {
                Columns =
                [
                    new() { Name = "p_id", Type = "int" },
                    new() { Name = "p_date", Type = "date" },
                    new() { Name = "p_note", Type = "string" }
                ],
                ColumnMapping = new Dictionary<string, int> { ["p_id"] = 0, ["p_date"] = 1, ["p_note"] = 2 }
            });

            stopwatch.Stop();
            var allocated = GC.GetTotalAllocatedBytes(true) - before;
            var processAfter = Process.GetCurrentProcess().PeakWorkingSet64;
            var stageSize = new FileInfo(result.Stage.Path).Length;

            Assert.Equal(rows, result.TotalRows);
            Assert.Equal(rows, result.ValidRows);
            Assert.Equal(100, result.Preview.Count);
            Assert.True(stopwatch.Elapsed < limit, $"Разбор {rows} строк занял {stopwatch.Elapsed}");

            output?.WriteLine($"rows={rows} time={stopwatch.ElapsedMilliseconds} мс " +
                              $"alloc={allocated / 1024.0 / 1024.0:F1} МБ " +
                              $"peakDelta={(processAfter - processBefore) / 1024.0 / 1024.0:F1} МБ " +
                              $"stage={stageSize / 1024.0 / 1024.0:F1} МБ");
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }
}

[Trait("Category", "T3")]
public sealed class InputScaleT3Tests(ITestOutputHelper output)
{
    [Fact]
    public void Is02_MillionRowsAreStreamedWithoutProportionalMemory()
    {
        InputScaleTests.Run(1_000_000, TimeSpan.FromMinutes(5), output);
    }
}
