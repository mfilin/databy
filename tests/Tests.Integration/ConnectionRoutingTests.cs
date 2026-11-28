using System.Text.Json;
using Databy.Reports.Catalog;
using Databy.Reports.Input;
using Databy.Reports.Services;
using Xunit;

namespace Tests.Integration;

/// <summary>
/// Cn08: отчёты ходят только в подключение, указанное в source, и не могут молча
/// выполниться в PostgreSQL при подключении другого типа.
/// </summary>
[Trait("Category", "T2")]
[Collection(PostgresCollection.Name)]
public sealed class ConnectionRoutingTests(PostgresFixture fixture)
{
    private static readonly IReadOnlyDictionary<string, object?> NoParameters =
        new Dictionary<string, object?>();

    [Fact]
    public async Task Cn08_PostgresReportWithoutSourceUsesDefaultConnection()
    {
        using var settings = new RoutingSettings(fixture);
        var report = LoadReport("mixed.yml", source: null);

        using var result = await new PostgresReportQueryService(settings.Value)
            .ExecuteAsync(report, NoParameters);

        Assert.Equal(3, result.RowCount);
    }

    [Fact]
    public async Task Cn08_PostgresReportWithMainSourceRuns()
    {
        using var settings = new RoutingSettings(fixture);
        var report = LoadReport("mixed.yml", "main");

        using var result = await new PostgresReportQueryService(settings.Value)
            .ExecuteAsync(report, NoParameters);

        Assert.Equal(3, result.RowCount);
    }

    [Fact]
    public async Task Cn08_PostgresReportWithClickHouseSourceIsRejected()
    {
        using var settings = new RoutingSettings(fixture);
        var report = LoadReport("mixed.yml", "ch");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new PostgresReportQueryService(settings.Value).ExecuteAsync(report, NoParameters));

        Assert.Contains("pq.mixed", exception.Message);
        Assert.Contains("'ch'", exception.Message);
        Assert.Contains("clickhouse", exception.Message);
        Assert.Contains("пока не поддерживается", exception.Message);
    }

    [Fact]
    public async Task Cn08_PostgresReportWithUnknownSourceListsKnownConnections()
    {
        using var settings = new RoutingSettings(fixture);
        var report = LoadReport("mixed.yml", "ghost");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new PostgresReportQueryService(settings.Value).ExecuteAsync(report, NoParameters));

        Assert.Contains("ghost", exception.Message);
        Assert.Contains("main", exception.Message);
        Assert.Contains("ch", exception.Message);
        Assert.Contains("DATABY_CONNECTION_GHOST", exception.Message);
    }

    [Fact]
    public async Task Cn08_ListReportWithMainSourceRuns()
    {
        using var settings = new RoutingSettings(fixture);
        using var input = Input(["7707083893", "500100732259", "9999999999"]);
        var report = LoadReport("list_by_inn.yml", "main");

        using var result = await new ListReportQueryService(settings.Value)
            .ExecuteAsync(report, Parameters(), input.Stage, null, CancellationToken.None);

        Assert.Equal(3, result.RowCount);
    }

    [Fact]
    public async Task Cn08_ListReportWithClickHouseSourceIsRejected()
    {
        using var settings = new RoutingSettings(fixture);
        using var input = Input(["7707083893"]);
        var report = LoadReport("list_by_inn.yml", "ch");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ListReportQueryService(settings.Value)
                .ExecuteAsync(report, Parameters(), input.Stage, null, CancellationToken.None));

        Assert.Contains("lst.by_inn", exception.Message);
        Assert.Contains("'ch'", exception.Message);
        Assert.Contains("clickhouse", exception.Message);
        Assert.Contains("пока не поддерживается", exception.Message);
    }

    [Fact]
    public async Task Cn08_ListReportWithUnknownSourceListsKnownConnections()
    {
        using var settings = new RoutingSettings(fixture);
        using var input = Input(["7707083893"]);
        var report = LoadReport("list_by_inn.yml", "ghost");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ListReportQueryService(settings.Value)
                .ExecuteAsync(report, Parameters(), input.Stage, null, CancellationToken.None));

        Assert.Contains("ghost", exception.Message);
        Assert.Contains("main", exception.Message);
        Assert.Contains("ch", exception.Message);
    }

    private ReportDefinition LoadReport(string file, string? source)
    {
        var directory = Path.Combine(
            fixture.TempDirectory, "routing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.Copy(Path.Combine(fixture.CatalogRoot, file), Path.Combine(directory, file), true);

        var catalog = new ReportCatalogLoader().Load(directory);
        Assert.Empty(catalog.Errors);
        var report = Assert.Single(catalog.Reports);
        report.Source = source;
        return report;
    }

    private static IReadOnlyDictionary<string, object?> Parameters() =>
        new Dictionary<string, object?> { ["p_asof"] = new DateOnly(2026, 1, 1) };

    private static InputScope Input(IReadOnlyList<string> inns)
    {
        var parse = new ListInputParser().ParseText(
            string.Join('\n', inns) + "\n",
            new InputParseOptions
            {
                Delimiter = InputDelimiter.None,
                Columns = [new InputColumnDefinition { Name = "p_inn", Type = "string" }],
                ColumnMapping = new Dictionary<string, int> { ["p_inn"] = 0 },
                MaxRows = 1_000_000
            });
        return new InputScope(parse);
    }

    private sealed class InputScope(InputParseResult result) : IDisposable
    {
        public InputStage Stage { get; } = result.Stage;

        public void Dispose() => result.Dispose();
    }

    /// <summary>
    /// Настройки из настоящего appsettings.local.json: так проверяются и сообщения
    /// ApplicationSettings. Переменные DATABY_CONNECTION* на время теста отключены.
    /// </summary>
    private sealed class RoutingSettings : IDisposable
    {
        private static readonly string[] EnvironmentVariables =
        [
            ApplicationSettings.ConnectionVariable,
            "DATABY_CONNECTION_MAIN",
            "DATABY_CONNECTION_CH"
        ];

        private readonly string _directory;
        private readonly Dictionary<string, string?> _previous = new(StringComparer.Ordinal);

        public RoutingSettings(PostgresFixture fixture)
        {
            foreach (var name in EnvironmentVariables)
            {
                _previous[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, null);
            }

            _directory = Path.Combine(
                Path.GetTempPath(), "databy-routing-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);

            var json = new
            {
                Connections = new Dictionary<string, object>
                {
                    ["main"] = new
                    {
                        Type = ConnectionTypes.Postgres,
                        ConnectionString = fixture.ConnectionString
                    },
                    ["ch"] = new
                    {
                        Type = ConnectionTypes.ClickHouse,
                        ConnectionString = "Host=clickhouse.invalid;Port=8123;Protocol=http;Database=default"
                    }
                },
                DefaultConnection = "main"
            };
            File.WriteAllText(
                Path.Combine(_directory, "appsettings.local.json"),
                JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true }));

            Value = new ApplicationSettings(_directory);
        }

        public ApplicationSettings Value { get; }

        public void Dispose()
        {
            foreach (var (name, value) in _previous)
                Environment.SetEnvironmentVariable(name, value);

            try { Directory.Delete(_directory, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
