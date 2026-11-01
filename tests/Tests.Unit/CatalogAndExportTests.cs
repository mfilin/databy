using System.Data;
using System.Globalization;
using System.Text;
using Databy.Reports.Catalog;
using Databy.Reports.Export;
using ExcelDataReader;

namespace Tests.Unit;

[Trait("Category", "T1")]
public sealed class CatalogTests
{
    [Fact]
    public void Cat01_LoadsValidReportsAndFolderTree()
    {
        using var dir = new TempDirectory();
        Write(dir.Path, "sales/monthly.yml", Valid("sales.monthly"));
        File.WriteAllText(Path.Combine(dir.Path, "catalog.version"), "2.4\n");
        var result = new ReportCatalogLoader().Load(dir.Path);
        Assert.Single(result.Reports);
        Assert.Equal("2.4", result.Version);
        Assert.Contains("sales", result.Folders);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Cat02_BrokenYamlReportsFileAndKeepsGoodReports()
    {
        using var dir = new TempDirectory();
        Write(dir.Path, "good.yml", Valid("good"));
        Write(dir.Path, "broken.yml", "code: [broken\n");
        var result = new ReportCatalogLoader().Load(dir.Path);
        Assert.Single(result.Reports);
        var error = Assert.Single(result.Errors);
        Assert.EndsWith("broken.yml", error.File);
    }

    [Theory]
    [InlineData("unknown-type", "Неизвестный тип")]
    [InlineData("missing-routine", "routine.name")]
    [InlineData("bad-role", "Недопустимая роль")]
    public void Cat03_RejectsSchemaViolations(string violation, string expectedError)
    {
        using var dir = new TempDirectory();
        var yaml = violation switch
        {
            "unknown-type" => Valid("bad").Replace("type: date", "type: nonsense", StringComparison.Ordinal),
            "missing-routine" => Valid("bad").Replace("  name: reports.fn_test", "  name: ''", StringComparison.Ordinal),
            "bad-role" => Valid("bad").Replace("roles: [admin, user]", "roles: [superuser]", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(violation))
        };
        Write(dir.Path, "bad.yml", yaml);
        var result = new ReportCatalogLoader().Load(dir.Path);
        Assert.Empty(result.Reports);
        Assert.Contains(expectedError, Assert.Single(result.Errors).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cat03_RejectsDuplicateCodes()
    {
        using var dir = new TempDirectory();
        Write(dir.Path, "one.yml", Valid("same")); Write(dir.Path, "two.yml", Valid("same"));
        var result = new ReportCatalogLoader().Load(dir.Path);
        Assert.Single(result.Reports);
        Assert.Contains("Повторяется code", Assert.Single(result.Errors).Message);
    }

    [Fact]
    public void Cat04_RealRepositoryCatalogPassesLint()
    {
        var catalog = FindRepositoryCatalog();
        var result = new ReportCatalogLoader().Load(catalog);
        Assert.NotEmpty(result.Reports);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Cat05_DefaultDateMacrosUseSuppliedToday()
    {
        var today = new DateOnly(2026, 11, 1);
        Assert.Equal(today, ReportCatalogLoader.ResolveDateDefault("today", today));
        Assert.Equal(new DateOnly(2026, 11, 1), ReportCatalogLoader.ResolveDateDefault("month_start", today));
        Assert.Equal(new DateOnly(2026, 1, 1), ReportCatalogLoader.ResolveDateDefault("2026-01-01", today));
    }

    internal static string Valid(string code) => $"""
        code: {code}
        title: Отчёт
        routine:
          name: reports.fn_test
          kind: function
        parameters:
          - name: p_date
            type: date
            label: Дата
            required: true
            default: today
        roles: [admin, user]
        """;

    internal static void Write(string root, string relative, string text)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    private static string FindRepositoryCatalog()
    {
        var path = new DirectoryInfo(AppContext.BaseDirectory);
        while (path is not null && !Directory.Exists(Path.Combine(path.FullName, "catalog"))) path = path.Parent;
        return path is null ? Path.Combine(AppContext.BaseDirectory, "catalog") : Path.Combine(path.FullName, "catalog");
    }
}

[Trait("Category", "T1")]
public sealed class ExportTests
{
    static ExportTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void Exp01_XlsxRoundTripsTypedValuesAndNullWithIndependentReader()
    {
        using var files = new TempDirectory();
        var table = Data();
        var stage = Stage(files.Path, table);
        var path = Path.Combine(files.Path, "result.xlsx");
        new ReportExportWriter().Save(stage, table, path, "Продажи", null, table.Rows.Count);
        using var reader = ExcelReaderFactory.CreateReader(File.OpenRead(path));
        Assert.True(reader.Read());
        Assert.Equal("date", reader.GetString(0));
        Assert.Equal("amount", reader.GetString(1));
        Assert.True(reader.Read());
        Assert.Equal(new DateTime(2026, 10, 1), Convert.ToDateTime(reader.GetValue(0), CultureInfo.InvariantCulture).Date);
        Assert.Equal(1234.5, Convert.ToDouble(reader.GetValue(1), CultureInfo.InvariantCulture));
        Assert.Equal("товар; \"тест\"\nстрока", reader.GetString(2));
        Assert.Null(reader.GetValue(3));
    }

    [Fact]
    public void Exp02_YamlHeadersFormatsAndHiddenColumnsAreApplied()
    {
        using var files = new TempDirectory();
        var table = Data();
        var stage = Stage(files.Path, table);
        var path = Path.Combine(files.Path, "result.xlsx");
        var options = new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["date"] = new() { Title = "Дата сделки" }, ["amount"] = new() { Title = "Итого", Format = "#,##0.00" }, ["secret"] = new() { Hidden = true }
        };
        new ReportExportWriter().Save(stage, table, path, "Продажи", options, table.Rows.Count);
        using var reader = ExcelReaderFactory.CreateReader(File.OpenRead(path));
        Assert.True(reader.Read());
        Assert.Equal(new[] { "Дата сделки", "Итого", "text", "empty" }, Enumerable.Range(0, reader.FieldCount).Select(reader.GetString).ToArray());
        Assert.True(reader.Read());
        Assert.Equal(1234.5, Convert.ToDouble(reader.GetValue(1), CultureInfo.InvariantCulture));
        Assert.Contains("0.00", reader.GetNumberFormatString(1));
    }

    [Fact]
    public void Exp03_SheetNameIsSanitizedAndLimitedTo31Characters()
    {
        var clean = ReportExportWriter.CleanSheetName("bad/name:*?[]\\long sheet name that is far too long");
        Assert.DoesNotContain('/', clean); Assert.DoesNotContain(':', clean); Assert.Equal(31, clean.Length);
    }

    [Fact]
    public void Exp04_RowLimitSelectsXlsxAtLimitAndCsvAboveIt()
    {
        using var files = new TempDirectory();
        var table = DataWithRows(5); var stage = Stage(files.Path, table);
        var atLimit = new ReportExportWriter().Save(stage, table, Path.Combine(files.Path, "at.xlsx"), "Rows", null, 5, 5);
        Assert.Equal("XLSX", atLimit.Format); Assert.False(atLimit.ExcelLimitExceeded);
        var overTable = DataWithRows(6); var overStage = Stage(files.Path, overTable);
        var above = new ReportExportWriter().Save(overStage, overTable, Path.Combine(files.Path, "over.xlsx"), "Rows", null, 6, 5);
        Assert.Equal("CSV", above.Format); Assert.True(above.ExcelLimitExceeded); Assert.EndsWith("over.csv", above.Path);
    }

    [Fact]
    public void Exp05_CsvHasBomSemicolonAndEscapesQuotesSeparatorsAndNewlines()
    {
        using var files = new TempDirectory();
        var table = Data(); var stage = Stage(files.Path, table);
        var path = Path.Combine(files.Path, "copy.csv");
        new ReportExportWriter().Save(stage, table, path, null, null, table.Rows.Count);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
        using var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(path, Encoding.UTF8)
        { TextFieldType = Microsoft.VisualBasic.FileIO.FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(";");
        _ = parser.ReadFields(); var row = parser.ReadFields()!;
        Assert.Equal("товар; \"тест\"\nстрока", row[2]);
        Assert.Equal(ReportExportWriter.NullMarker, row[3]);
    }

    [Fact]
    public void Exp06_OutputValuesDoNotDependOnCurrentCulture()
    {
        using var files = new TempDirectory();
        var original = CultureInfo.CurrentCulture;
        try
        {
            var table = Data(); var stage = Stage(files.Path, table);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            var first = new ReportExportWriter().Save(stage, table, Path.Combine(files.Path, "ru.xlsx"), "Rows", null, table.Rows.Count);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var second = new ReportExportWriter().Save(stage, table, Path.Combine(files.Path, "en.xlsx"), "Rows", null, table.Rows.Count);
            Assert.Equal(ReadAmount(first.Path), ReadAmount(second.Path));
            Assert.Equal(1234.5, ReadAmount(first.Path));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public void Exp07_StagedCsvIsRetainedOnSuccessAndRemovedByOwnerOnDispose()
    {
        using var files = new TempDirectory();
        var table = Data();
        string stagePath;
        using (var stage = new TemporaryCsvStage())
        {
            stagePath = stage.Path;
            using (var writer = stage.OpenWriter())
            {
                ReportExportWriter.WriteCsvRecord(writer, table.Columns.Cast<DataColumn>().Select(c => c.ColumnName));
                foreach (DataRow row in table.Rows) ReportExportWriter.WriteStageRow(writer, row);
            }
            new ReportExportWriter().Save(stage.Path, table, Path.Combine(files.Path, "out.xlsx"), "Rows", null, 1);
            Assert.True(File.Exists(stage.Path)); // kept while the report result is still active
        }
        Assert.False(File.Exists(stagePath));

        string errorPath;
        using (var failedStage = new TemporaryCsvStage())
        {
            errorPath = failedStage.Path;
            using (var writer = failedStage.OpenWriter()) { writer.WriteLine("\"date\";\"amount\";\"text\";\"empty\";\"secret\""); writer.WriteLine("\"only-one-column\""); }
            Assert.Throws<InvalidDataException>(() => new ReportExportWriter().Save(failedStage.Path, table,
                Path.Combine(files.Path, "broken.xlsx"), "Rows", null, 1));
        }
        Assert.False(File.Exists(errorPath));

        string canceledPath;
        using (var canceledStage = new TemporaryCsvStage())
        {
            canceledPath = canceledStage.Path;
            using var cts = new CancellationTokenSource(); cts.Cancel();
            Assert.Throws<OperationCanceledException>(() => cts.Token.ThrowIfCancellationRequested());
        }
        Assert.False(File.Exists(canceledPath));
    }

    private static double ReadAmount(string path)
    {
        using var reader = ExcelReaderFactory.CreateReader(File.OpenRead(path));
        reader.Read(); reader.Read(); return Convert.ToDouble(reader.GetValue(1), CultureInfo.InvariantCulture);
    }

    private static DataTable Data()
    {
        var table = new DataTable();
        table.Columns.Add("date", typeof(DateTime)); table.Columns.Add("amount", typeof(decimal));
        table.Columns.Add("text", typeof(string)); table.Columns.Add("empty", typeof(string)); table.Columns.Add("secret", typeof(string));
        table.Rows.Add(new DateTime(2026, 10, 1), 1234.5m, "товар; \"тест\"\nстрока", DBNull.Value, "hidden");
        return table;
    }

    private static DataTable DataWithRows(int count)
    {
        var table = Data();
        for (var i = 1; i < count; i++) table.Rows.Add(new DateTime(2026, 10, 1).AddDays(i), (decimal)i, $"row {i}", DBNull.Value, "hidden");
        return table;
    }

    private static string Stage(string directory, DataTable table)
    {
        var path = Path.Combine(directory, "stage.csv");
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        ReportExportWriter.WriteCsvRecord(writer, table.Columns.Cast<DataColumn>().Select(column => column.ColumnName));
        foreach (DataRow row in table.Rows) ReportExportWriter.WriteStageRow(writer, row);
        return path;
    }
}

internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "databy-tests-" + Guid.NewGuid().ToString("N"));
    public TempDirectory() => Directory.CreateDirectory(Path);
    public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
}
