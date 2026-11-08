using System.Diagnostics;
using System.Globalization;
using Databy.Reports.Catalog;
using Databy.Reports.Input;
using Databy.Reports.Services;
using Npgsql;
using Xunit;

namespace Tests.Integration;

[Trait("Category", "T2")]
[Collection(PostgresCollection.Name)]
public sealed class ListReportQueryServiceTests(PostgresFixture fixture)
{
    private static readonly string[] OrgInns = ["7707083893", "500100732259"];

    [Fact]
    public async Task Ls01_ScenarioAKeyAndCommonParameter()
    {
        var report = Load("list_by_inn.yml");
        using var input = Input(["7707083893", "500100732259", "9999999999"]);
        using var service = new ServiceScope(fixture);

        var result = await service.ExecuteAsync(report, Parameters("2026-01-01"), input.Stage);

        Assert.Equal(3, result.RowCount);
        var rows = ReadResult(result);
        Assert.Equal(["Найдено", "Найдено", "Не найдено"], rows.Select(row => row[^1]));
        Assert.Equal("Ромашка", rows[0][2]);
        Assert.Equal("Казань", rows[1][3]);
    }

    [Fact]
    public async Task Ls02_ScenarioBPerRowDate()
    {
        var report = Load("list_by_inn_date.yml");
        using var input = Input(["7707083893", "500100732259"], dates: ["2026-01-15", "2027-01-15"]);
        using var service = new ServiceScope(fixture);

        var result = await service.ExecuteAsync(report, Parameters("2026-06-01"), input.Stage);
        var rows = ReadResult(result);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Найдено", rows[0][^1]);
        Assert.Equal("Не найдено", rows[1][^1]);
    }

    [Fact]
    public async Task Ls03_OneToManyRepeatsSourceRow()
    {
        var report = Load("list_one_to_many.yml");
        using var input = Input(["7707083893"]);
        using var service = new ServiceScope(fixture);

        var result = await service.ExecuteAsync(report, Parameters(), input.Stage);
        var rows = ReadResult(result);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal("7707083893", row[0]));
        Assert.Equal(["A", "B"], rows.Select(row => row[2]));
    }

    [Fact]
    public async Task Ls04_ErrorRowsStayAndAreNotSent()
    {
        var report = Load("list_bad_date.yml");
        using var input = InputWithDates(["7707083893", "500100732259"], ["2026-01-01", "not-a-date"]);
        using var service = new ServiceScope(fixture);

        var result = await service.ExecuteAsync(report, Parameters("2026-01-01"), input.Stage);
        var rows = ReadResult(result);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Найдено", rows[0][^1]);
        Assert.StartsWith("Ошибка во входных данных", rows[1][^1]);
    }

    [Fact]
    public async Task Ls05_AllRowsInvalidStillGetsSchema()
    {
        var report = Load("list_bad_date.yml");
        using var input = InputWithDates(["7707083893", "500100732259"], ["not-a-date", "also-bad"]);
        using var service = new ServiceScope(fixture);

        var result = await service.ExecuteAsync(report, Parameters("2026-01-01"), input.Stage);
        var rows = ReadResult(result);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.StartsWith("Ошибка во входных данных", row[^1]));
        Assert.Contains("name", result.Preview.Table.Columns.Cast<System.Data.DataColumn>().Select(column => column.ColumnName));
    }

    [Fact]
    public async Task Ls06_BatchesWithBoundaryNotFoundRow()
    {
        var report = LoadWithBatch("list_found_only.yml", batchSize: 10);
        var inns = Enumerable.Range(0, 25).Select(index => index % 3 == 0 ? "7707083893" : "missing" + index).ToArray();
        using var input = Input(inns);
        using var service = new ServiceScope(fixture);

        var progress = new List<ListQueryProgress>();
        var result = await service.ExecuteAsync(report, Parameters(), input.Stage, new Progress<ListQueryProgress>(progress.Add));

        var rows = ReadResult(result);
        Assert.Equal(25, rows.Count);
        Assert.Equal(25, result.RowCount);
        Assert.Equal(3, progress.Max(item => item.BatchNumber));
        Assert.Equal(25, progress[^1].ProcessedRows);
    }

    [Fact]
    public async Task Ls07_UnsortedResultIsRejected()
    {
        var report = Load("list_unsorted.yml");
        using var input = Input(["7707083893", "500100732259"]);
        using var service = new ServiceScope(fixture);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ExecuteAsync(report, Parameters(), input.Stage));

        Assert.Contains("не отсортирован", ex.Message);
    }

    [Fact]
    public async Task Ls08_ForeignRowIdIsRejected()
    {
        var report = Load("list_foreign.yml");
        using var input = Input(["7707083893"]);
        using var service = new ServiceScope(fixture);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ExecuteAsync(report, Parameters(), input.Stage));

        Assert.Contains("чужой row_id", ex.Message);
    }

    [Fact]
    public async Task Ls09_OverloadedFunctionIsRejected()
    {
        var report = Load("list_overloaded.yml");
        using var input = Input(["7707083893"]);
        using var service = new ServiceScope(fixture);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ExecuteAsync(report, Parameters(), input.Stage));

        Assert.Contains("перегружена", ex.Message);
    }

    [Fact]
    public async Task Ls10_CancellationDeletesResultStage()
    {
        var report = Load("list_slow.yml");
        using var input = Input(["7707083893"]);
        using var service = new ServiceScope(fixture);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ExecuteAsync(report, Parameters(), input.Stage, null, cts.Token));

        Assert.True(TempStageTracker.CountStagedFiles() <= fixture.StagedFilesOnStart + 1);
    }

    [Fact]
    public async Task Ls11_HundredThousandRowsInBatches()
    {
        var report = LoadWithBatch("list_found_only.yml", batchSize: 10_000);
        var inns = Enumerable.Range(0, 100_000).Select(index => index % 10 == 0 ? "7707083893" : "missing").ToArray();
        using var input = Input(inns);
        using var service = new ServiceScope(fixture);
        var stopwatch = Stopwatch.StartNew();

        var result = await service.ExecuteAsync(report, Parameters(), input.Stage);

        stopwatch.Stop();
        Assert.Equal(100_000, result.RowCount);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMinutes(3), $"Обработка 100 000 строк заняла {stopwatch.Elapsed}");
    }

    private ReportDefinition Load(string file) => LoadWithBatch(file, batchSize: null);

    private ReportDefinition LoadWithBatch(string file, int? batchSize)
    {
        var directory = Path.Combine(fixture.TempDirectory, "catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var source = Path.Combine(fixture.CatalogRoot, file);
        var target = Path.Combine(directory, file);
        File.Copy(source, target, true);

        var catalog = new ReportCatalogLoader().Load(directory);
        Assert.Empty(catalog.Errors);
        var report = Assert.Single(catalog.Reports);
        if (batchSize is { } size) report.Input!.BatchSize = size;
        return report;
    }

    private static IReadOnlyDictionary<string, object?> Parameters(string? asof = null) =>
        asof is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?> { ["p_asof"] = DateOnly.ParseExact(asof, "yyyy-MM-dd", CultureInfo.InvariantCulture) };

    private static InputScope InputWithDates(IReadOnlyList<string> inns, IReadOnlyList<string> dates) => Input(inns, dates);

    private static InputScope Input(IReadOnlyList<string> inns, IReadOnlyList<string>? dates = null)
    {
        var columns = dates is null
            ? new List<InputColumnDefinition> { new() { Name = "p_inn", Type = "string" } }
            : new List<InputColumnDefinition> { new() { Name = "p_inn", Type = "string" }, new() { Name = "p_dt", Type = "date" } };

        var mapping = new Dictionary<string, int> { ["p_inn"] = 0 };
        if (dates is not null) mapping["p_dt"] = 1;

        var text = new System.Text.StringBuilder();
        for (var i = 0; i < inns.Count; i++)
        {
            text.Append(inns[i]);
            if (dates is not null) text.Append('\t').Append(dates[i]);
            text.Append('\n');
        }

        var parse = new ListInputParser().ParseText(text.ToString(), new InputParseOptions
        {
            Delimiter = dates is null ? InputDelimiter.None : InputDelimiter.Tab,
            Columns = columns,
            ColumnMapping = mapping,
            MaxRows = 1_000_000
        });
        return new InputScope(parse);
    }

    private static List<string[]> ReadResult(ReportQueryResult result) =>
        result.Stage is null
            ? []
            : File.ReadAllLines(result.Stage.Path, System.Text.Encoding.UTF8)
                .Where(line => line.Length > 0)
                .Skip(1)
                .Select(SplitRecord)
                .ToList();

    private static string[] SplitRecord(string line)
    {
        var fields = new List<string>();
        var builder = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { builder.Append('"'); i++; }
                    else inQuotes = false;
                }
                else builder.Append(ch);
                continue;
            }
            if (ch == '"') { inQuotes = true; continue; }
            if (ch == ';') { fields.Add(builder.ToString()); builder.Clear(); continue; }
            builder.Append(ch);
        }
        fields.Add(builder.ToString());
        return fields.ToArray();
    }

    private sealed class InputScope(InputParseResult result) : IDisposable
    {
        public InputStage Stage { get; } = result.Stage;

        public void Dispose() => result.Dispose();
    }

    private sealed class ServiceScope : IDisposable
    {
        private readonly List<ReportQueryResult> _results = [];

        public ServiceScope(PostgresFixture fixture) => Service = new ListReportQueryService(fixture.Settings);

        public ListReportQueryService Service { get; }

        public async Task<ReportQueryResult> ExecuteAsync(ReportDefinition report,
            IReadOnlyDictionary<string, object?> parameters, InputStage input,
            IProgress<ListQueryProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var result = await Service.ExecuteAsync(report, parameters, input, progress, cancellationToken);
            _results.Add(result);
            return result;
        }

        public void Dispose()
        {
            foreach (var result in _results) result.Dispose();
        }
    }
}
