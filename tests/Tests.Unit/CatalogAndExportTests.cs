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

    [Theory]
    [InlineData("kind-procedure", "  kind: function", "  kind: procedure", "routine.kind")]
    [InlineData("lookup-type", "type: date", "type: lookup", "пока не поддерживается")]
    [InlineData("daterange-type", "type: date", "type: daterange", "пока не поддерживается")]
    [InlineData("duplicate-param", "  - name: p_date", "  - name: p_date\n    type: date\n    label: Дубль\n  - name: p_date", "Повторяется имя параметра")]
    [InlineData("bad-param-name", "  - name: p_date", "  - name: p-bad", "Недопустимое имя параметра")]
    [InlineData("zero-timeout", "  kind: function", "  kind: function\n  timeout_sec: 0", "timeout_sec")]
    public void Cat06_RejectsUnsupportedRoutineAndParameterShape(string violation, string search, string replace, string expectedError)
    {
        using var dir = new TempDirectory();
        Write(dir.Path, "good.yml", Valid("good"));
        Write(dir.Path, "bad.yml", Valid("bad").Replace(search, replace, StringComparison.Ordinal));
        var result = new ReportCatalogLoader().Load(dir.Path);
        Assert.Single(result.Reports);
        Assert.Equal("good", result.Reports[0].Code);
        Assert.Contains(expectedError, Assert.Single(result.Errors).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cat07_InvalidTypedDefaultReportsFileErrorAndKeepsOtherReports()
    {
        using var dir = new TempDirectory();
        Write(dir.Path, "good.yml", Valid("good"));
        Write(dir.Path, "bad.yml", Valid("bad")
            .Replace("type: date", "type: int", StringComparison.Ordinal)
            .Replace("default: today", "default: abc", StringComparison.Ordinal));
        var result = new ReportCatalogLoader().Load(dir.Path);
        Assert.Equal("good", Assert.Single(result.Reports).Code);
        var error = Assert.Single(result.Errors);
        Assert.EndsWith("bad.yml", error.File);
        Assert.Contains("p_date", error.Message);
        Assert.Contains("int", error.Message);
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
            ["date"] = new() { Title = "Дата сделки" },
            ["amount"] = new() { Title = "Итого", Format = "#,##0.00" },
            ["secret"] = new() { Hidden = true }
        };
        new ReportExportWriter().Save(stage, table, path, "Продажи", new ResultDefinition { Columns = options }, table.Rows.Count);
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
        Assert.Contains("\r\n", Encoding.UTF8.GetString(bytes));
        var (header, row) = ReadCsvRows(path, 2);
        Assert.Equal(new[] { "date", "amount", "text", "empty", "secret" }, header);
        Assert.Equal("товар; \"тест\"\nстрока", row[2]);
        Assert.Equal(string.Empty, row[3]);
        Assert.DoesNotContain(ReportExportWriter.NullMarker, File.ReadAllText(path, Encoding.UTF8));
        Assert.Equal("1234.5", row[1]);
    }

    [Fact]
    public void Exp08_CsvUsesYamlHeadersAndDropsHiddenColumns()
    {
        using var files = new TempDirectory();
        var table = Data(); var stage = Stage(files.Path, table);
        var path = Path.Combine(files.Path, "yaml.csv");
        var options = new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["date"] = new() { Title = "Дата сделки" },
            ["amount"] = new() { Title = "Итого" },
            ["empty"] = new() { Title = "Пусто" },
            ["secret"] = new() { Hidden = true }
        };
        new ReportExportWriter().Save(stage, table, path, null, new ResultDefinition { Columns = options }, table.Rows.Count);
        var (header, row) = ReadCsvRows(path, 2);
        Assert.Equal(new[] { "Дата сделки", "Итого", "text", "Пусто" }, header);
        Assert.DoesNotContain("secret", string.Join(";", header));
        Assert.Equal(row.Length, header.Length);
    }

    [Fact]
    public void Exp09_CsvWritesIsoDatesAndInvariantNumbersInRequiredFormat()
    {
        using var files = new TempDirectory();
        var table = new DataTable();
        table.Columns.Add("d", typeof(DateOnly)); table.Columns.Add("dt", typeof(DateTime));
        table.Columns.Add("num", typeof(decimal)); table.Columns.Add("gap", typeof(string));
        table.Rows.Add(new DateOnly(2026, 3, 5), new DateTime(2026, 3, 5, 14, 30, 59), 1234.567m, DBNull.Value);
        var stage = Stage(files.Path, table);
        var path = Path.Combine(files.Path, "iso.csv");
        new ReportExportWriter().Save(stage, table, path, null, null, 1);
        var (_, row) = ReadCsvRows(path, 2);
        Assert.Equal("2026-03-05", row[0]);
        Assert.Equal("2026-03-05 14:30:59", row[1]);
        Assert.Equal("1234.567", row[2]);
        Assert.Equal(string.Empty, row[3]);
    }

    [Fact]
    public void Exp10_CsvQuotesOnlyFieldsWithSeparatorsQuotesOrBreaks()
    {
        using var files = new TempDirectory();
        var table = new DataTable();
        table.Columns.Add("plain", typeof(string)); table.Columns.Add("semi", typeof(string));
        table.Columns.Add("quote", typeof(string)); table.Columns.Add("line", typeof(string));
        table.Rows.Add("ok", "a;b", "s\"q", "x\r\ny");
        var stage = Stage(files.Path, table);
        var path = Path.Combine(files.Path, "quote.csv");
        new ReportExportWriter().Save(stage, table, path, null, null, 1);
        var text = File.ReadAllText(path, Encoding.UTF8);
        var line = text.Split("\r\n")[1];
        Assert.StartsWith("ok;\"a;b\";\"s\"\"q\";", line);
        var (_, row) = ReadCsvRows(path, 2);
        Assert.Equal(new[] { "ok", "a;b", "s\"q", "x\r\ny" }, row);
    }

    [Fact]
    public void Exp11_CsvAboveExcelLimitIsAlsoCleaned()
    {
        using var files = new TempDirectory();
        var table = DataWithRows(6); var stage = Stage(files.Path, table);
        var result = new ReportExportWriter().Save(stage, table, Path.Combine(files.Path, "over.xlsx"), "Rows", null, 6, 5);
        Assert.True(result.ExcelLimitExceeded);
        var (header, row) = ReadCsvRows(result.Path, 2);
        Assert.Equal(new[] { "date", "amount", "text", "empty", "secret" }, header);
        Assert.Equal(string.Empty, row[3]);
        Assert.StartsWith("2026-10-01", row[0]);
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

    [Fact]
    public void Exp12_LargeCsvIsStreamedWithoutHoldingWholeFileInMemory()
    {
        using var files = new TempDirectory();
        var stage = Path.Combine(files.Path, "large-stage.csv");
        const int rows = 100_000;
        using (var writer = new StreamWriter(stage, false, new UTF8Encoding(true)))
        {
            ReportExportWriter.WriteCsvRecord(writer, new[] { "id", "text" });
            for (var i = 0; i < rows; i++) ReportExportWriter.WriteCsvRecord(writer, new[] { i.ToString(CultureInfo.InvariantCulture), "row " + i });
        }
        var schema = new DataTable();
        schema.Columns.Add("id", typeof(int));
        schema.Columns.Add("text", typeof(string));
        var target = Path.Combine(files.Path, "large.csv");

        var before = GC.GetTotalMemory(forceFullCollection: true);
        var result = new ReportExportWriter().Save(stage, schema, target, null, null, rows);
        var after = GC.GetTotalMemory(forceFullCollection: true);

        Assert.Equal("CSV", result.Format);
        Assert.Equal(rows, result.Rows);
        Assert.True(new FileInfo(target).Length > rows, "CSV должен содержать все строки");
        using var reader = new StreamReader(target, Encoding.UTF8);
        var lineCount = 0;
        while (reader.ReadLine() is not null) lineCount++;
        Assert.Equal(rows + 1, lineCount);
        Assert.True(after - before < 64L * 1024 * 1024, $"Рост памяти: {after - before} байт");
    }

    private static (string[] Header, string[] Row) ReadCsvRows(string path, int rows)
    {
        using var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(path, Encoding.UTF8)
        { TextFieldType = Microsoft.VisualBasic.FileIO.FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(";");
        var header = parser.ReadFields()!;
        string[]? row = null;
        for (var i = 1; i < rows; i++) row = parser.ReadFields()!;
        return (header, row ?? []);
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
