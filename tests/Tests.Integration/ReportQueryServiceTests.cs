using System.Data;
using System.Diagnostics;
using System.Text;
using Databy.Reports.Catalog;
using Databy.Reports.Export;
using Databy.Reports.Services;
using Npgsql;
using Xunit;

namespace Tests.Integration;

[Trait("Category", "T2")]
[Collection(PostgresCollection.Name)]
public sealed class ReportQueryServiceTests(PostgresFixture fixture)
{
    private static IReadOnlyDictionary<string, object?> NoParameters => new Dictionary<string, object?>();

    [Fact]
    public async Task PQ01_TypedColumnsValuesAndStageFile()
    {
        var result = await Run("mixed.yml", NoParameters);
        using (result)
        {
            Assert.Equal(3, result.RowCount);
            Assert.Equal(3L, result.Preview.TotalRows);
            Assert.Equal(["id", "label", "amount", "happened", "enabled", "ratio"], ColumnNames(result));
            Assert.Equal(typeof(int), result.Preview.Table.Columns["id"]!.DataType);
            Assert.Equal(typeof(string), result.Preview.Table.Columns["label"]!.DataType);
            Assert.Equal(typeof(DateTime), result.Preview.Table.Columns["happened"]!.DataType);
            Assert.Equal(typeof(bool), result.Preview.Table.Columns["enabled"]!.DataType);

            Assert.Equal(3, result.Preview.Table.Rows.Count);
            Assert.Equal(1, result.Preview.Table.Rows[0]["id"]);
            Assert.Equal("alpha", result.Preview.Table.Rows[0]["label"]);
            Assert.Equal(12.50m, result.Preview.Table.Rows[0]["amount"]);
            Assert.Equal(new DateTime(2026, 1, 3), result.Preview.Table.Rows[0]["happened"]);
            Assert.Equal(true, result.Preview.Table.Rows[0]["enabled"]);
            Assert.Equal(DBNull.Value, result.Preview.Table.Rows[1]["amount"]);

            var stage = ReadStage(result.Stage.Path);
            Assert.Equal("\"id\";\"label\";\"amount\";\"happened\";\"enabled\";\"ratio\"", stage[0]);
            Assert.Equal("\"2\";\"beta\";\"" + ReportExportWriter.NullMarker + "\";\"02/04/2026 00:00:00\";\"False\";\"" + ReportExportWriter.NullMarker + "\"", stage[2]);
            Assert.Equal(4, stage.Count);
        }
    }

    [Fact]
    public async Task PQ02_AllParameterTypesArePassedVerbatim()
    {
        var parameters = new Dictionary<string, object?>
        {
            ["p_date"] = new DateOnly(2026, 4, 5),
            ["p_int"] = 42,
            ["p_decimal"] = 12.75m,
            ["p_text"] = "Привет, мир",
            ["p_bool"] = true
        };

        var result = await Run("params.yml", parameters);
        using (result)
        {
            var row = Assert.Single(result.Preview.Table.Rows.Cast<DataRow>());
            Assert.Equal(new DateTime(2026, 4, 5), row["echo_date"]);
            Assert.Equal(42, row["echo_int"]);
            Assert.Equal(12.75m, row["echo_decimal"]);
            Assert.Equal("Привет, мир", row["echo_text"]);
            Assert.Equal(true, row["echo_bool"]);
            Assert.Equal(DBNull.Value, row["echo_optional"]);
        }
    }

    [Fact]
    public async Task PQ03_ReportCatalogDemoReportsRun()
    {
        var expected = new Dictionary<string, string[]>
        {
            ["sales.monthly"] = ["sold_at", "product", "category", "quantity", "unit_price", "total", "region"],
            ["leaders.sales"] = ["product", "category", "quantity", "revenue", "deals"],
            ["leaders.city"] = ["region", "quantity", "revenue", "deals", "top_product"]
        };

        foreach (var (code, columns) in expected)
        {
            var definition = LoadDemoReport(code);
            using var result = await fixture.CreateService().ExecuteAsync(definition, ReportPeriod(definition));
            Assert.Equal(columns, ColumnNames(result));
            Assert.True(result.RowCount > 0, $"Отчёт {code} вернул пустой результат");
        }
    }

    [Fact]
    public async Task PQ04_CyrillicColumnNamesAndCharacterPaddingArePreserved()
    {
        var cyrillic = await Run("cyrillic.yml", NoParameters);
        using (cyrillic)
        {
            Assert.Equal(["ОГРН", "ИНН", "Наименование организации", "name_", "name_full_"], ColumnNames(cyrillic));
            var row = Assert.Single(cyrillic.Preview.Table.Rows.Cast<DataRow>());
            Assert.Equal("ООО Ромашка", row["Наименование организации"]);
        }

        var character = await Run("character.yml", NoParameters);
        using (character)
        {
            var row = Assert.Single(character.Preview.Table.Rows.Cast<DataRow>());
            // Характеризующий тест: character(10) возвращается дополненным пробелами.
            Assert.Equal("abc       ", row["padded"]);
            Assert.Equal(10, ((string)row["padded"]!).Length);
            Assert.Equal("abc", row["plain"]);
        }
    }

    [Fact]
    public async Task PQ05_MissingRoutineFailsBeforeQuery()
    {
        var definition = LoadFixture("missing_routine.yml");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.CreateService().ExecuteAsync(definition, NoParameters));

        Assert.Contains("отсутствует в PostgreSQL", ex.Message);
    }

    [Fact]
    public async Task PQ06_ParameterNameMismatchIsReported()
    {
        var definition = LoadFixture("wrong_param.yml");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.CreateService().ExecuteAsync(definition, new Dictionary<string, object?> { ["p_expected"] = 1 }));

        Assert.Contains("p_expected", ex.Message);
    }

    [Fact(Skip = "TASK-017: учёт proargmodes")]
    public void PQ06_OutputColumnNameIsNotTreatedAsParameter()
    {
    }

    [Fact]
    public async Task PQ07_SqlLookingParameterIsDataAndTableSurvives()
    {
        var parameters = new Dictionary<string, object?>
        {
            ["p_date"] = new DateOnly(2026, 1, 1),
            ["p_int"] = 1,
            ["p_decimal"] = 1.0m,
            ["p_text"] = "'; DROP TABLE items;--",
            ["p_bool"] = false
        };

        var result = await Run("params.yml", parameters);
        using (result)
        {
            var row = Assert.Single(result.Preview.Table.Rows.Cast<DataRow>());
            Assert.Equal("'; DROP TABLE items;--", row["echo_text"]);
        }

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var verify = new NpgsqlCommand("SELECT count(*) FROM items", connection);
        Assert.Equal(2L, (long)(await verify.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task PQ08_CancellationStopsQueryAndClearsBackend()
    {
        var definition = LoadFixture("slow.yml");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.CreateService().ExecuteAsync(definition, NoParameters, cts.Token));

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Отмена заняла {stopwatch.Elapsed}");

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var active = new NpgsqlCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND state = 'active' AND query LIKE '%pq.slow_report%' AND pid <> pg_backend_pid()",
            connection);
        Assert.Equal(0L, (long)(await active.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task PQ09_TimeoutRaisesTimeoutException()
    {
        var definition = LoadFixture("slow5.yml", timeoutSec: 1);
        var stopwatch = Stopwatch.StartNew();

        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => fixture.CreateService().ExecuteAsync(definition, NoParameters));

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Таймаут занял {stopwatch.Elapsed}");
        Assert.True(ex is NpgsqlException or TimeoutException, $"Неожиданное исключение: {ex.GetType().Name}");
    }

    [Fact]
    public async Task PQ10_DatabaseErrorLeavesNoStagedFile()
    {
        var definition = LoadFixture("failing.yml");
        var before = TempStageTracker.CountStagedFiles();

        var ex = await Assert.ThrowsAsync<PostgresException>(
            () => fixture.CreateService().ExecuteAsync(definition, NoParameters));

        Assert.Contains("pq synthetic failure", ex.MessageText);
        Assert.Equal(before, TempStageTracker.CountStagedFiles());
    }

    [Fact]
    public async Task PQ11_EmptyResultKeepsSchemaAndHeaderOnlyStage()
    {
        var result = await Run("empty.yml", NoParameters);
        using (result)
        {
            Assert.Equal(["id", "label"], ColumnNames(result));
            Assert.Equal(0, result.RowCount);
            Assert.Equal(0, result.Preview.Table.Rows.Count);
            var stage = ReadStage(result.Stage.Path);
            Assert.Equal(["\"id\";\"label\""], stage);
        }
    }

    [Fact]
    public async Task PQ12_TempTableFunctionRunsRepeatedlyAndInParallel()
    {
        var definition = LoadFixture("temp.yml");

        for (var i = 0; i < 2; i++)
        {
            using var sequential = await fixture.CreateService().ExecuteAsync(definition, new Dictionary<string, object?> { ["p_seed"] = i * 10 });
            Assert.Equal(2, sequential.RowCount);
        }

        var parallel = Enumerable.Range(0, 3)
            .Select(seed => fixture.CreateService().ExecuteAsync(definition, new Dictionary<string, object?> { ["p_seed"] = seed }))
            .ToArray();
        var results = await Task.WhenAll(parallel);
        try
        {
            Assert.All(results, result => Assert.Equal(2, result.RowCount));
        }
        finally
        {
            foreach (var result in results) result.Dispose();
        }
    }

    [Fact]
    public async Task PQ13_PreviewIsLimitedButStageHasAllRows()
    {
        var result = await Run("many_rows.yml", new Dictionary<string, object?> { ["p_count"] = 6000 });
        using (result)
        {
            Assert.Equal(PostgresReportQueryService.PreviewRowLimit, result.Preview.Table.Rows.Count);
            Assert.Equal(6000L, result.Preview.TotalRows);
            Assert.Equal(6000L, result.RowCount);
            Assert.Equal(6001, ReadStage(result.Stage.Path).Count);
        }
    }

    [Fact]
    public async Task PQ14_ExportMatchesQueryResult()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var definition = LoadFixture("mixed.yml");
        string? xlsxPath = null;
        string? csvPath = null;
        try
        {
            var result = await fixture.CreateService().ExecuteAsync(definition, NoParameters);
            using (result)
            {
                var directory = fixture.TempDirectory;
                xlsxPath = Path.Combine(directory, "pq14.xlsx");
                csvPath = Path.Combine(directory, "pq14.csv");

                new ReportExportWriter().Save(result.Stage.Path, result.Preview.Table, xlsxPath, "PQ", null, result.RowCount);
                new ReportExportWriter().Save(result.Stage.Path, result.Preview.Table, csvPath, null, null, result.RowCount);

                Assert.Equal(ColumnNames(result), ReadXlsxHeaders(xlsxPath));
                Assert.True(File.Exists(csvPath));
            }

            Assert.True(File.Exists(xlsxPath));
            var csvLines = File.ReadAllLines(csvPath, Encoding.UTF8);
            Assert.Equal("id;label;amount;happened;enabled;ratio", csvLines[0]);
            Assert.Equal("2;beta;;2026-02-04;False;", csvLines[2]);
        }
        finally
        {
            if (xlsxPath is not null) TryDelete(xlsxPath);
            if (csvPath is not null) TryDelete(csvPath);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }

    private async Task<ReportQueryResult> Run(string fixtureFile, IReadOnlyDictionary<string, object?> parameters)
    {
        var definition = LoadFixture(fixtureFile);
        return await fixture.CreateService().ExecuteAsync(definition, parameters);
    }

    private ReportDefinition LoadFixture(string file, int? timeoutSec = null)
    {
        var definition = LoadCatalogFile(file);
        if (timeoutSec is { } seconds) definition.Routine.TimeoutSec = seconds;
        return definition;
    }

    private ReportDefinition LoadCatalogFile(string file)
    {
        var path = Path.Combine(fixture.CatalogRoot, file);
        var single = Path.Combine(fixture.TempDirectory, "single-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(single);
        var copy = Path.Combine(single, Path.GetFileName(path));
        File.Copy(path, copy, true);

        var catalog = new ReportCatalogLoader().Load(single);
        Assert.Empty(catalog.Errors);
        return Assert.Single(catalog.Reports);
    }

    private static ReportDefinition LoadDemoReport(string code)
    {
        var repository = FindRepositoryCatalog();
        var catalog = new ReportCatalogLoader().Load(repository);
        Assert.Empty(catalog.Errors);
        return catalog.Reports.Single(report => report.Code == code);
    }

    private static string FindRepositoryCatalog()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "catalog");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "catalog.version"))) return candidate;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("catalog не найден");
    }

    private static Dictionary<string, object?> ReportPeriod(ReportDefinition definition)
    {
        var values = new Dictionary<string, object?>();
        var dates = definition.Parameters.Where(parameter => parameter.Type == "date").ToArray();
        if (dates.Length > 0) values[dates[0].Name] = new DateOnly(2026, 10, 1);
        if (dates.Length > 1) values[dates[1].Name] = new DateOnly(2026, 10, 31);
        foreach (var parameter in definition.Parameters.Where(parameter => parameter.Type == "int" && parameter.Required))
            values[parameter.Name] = 5;
        return values;
    }

    private static string[] ColumnNames(ReportQueryResult result) =>
        result.Preview.Table.Columns.Cast<DataColumn>().Select(column => column.ColumnName).ToArray();

    private static List<string> ReadStage(string path) =>
        File.ReadAllLines(path, Encoding.UTF8).Where(line => line.Length > 0).ToList();

    private static string[] ReadXlsxHeaders(string path)
    {
        using var reader = ExcelDataReader.ExcelReaderFactory.CreateReader(File.OpenRead(path));
        Assert.True(reader.Read());
        return Enumerable.Range(0, reader.FieldCount).Select(reader.GetString).ToArray();
    }
}
