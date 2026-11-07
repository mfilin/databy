using System.Data;
using System.Text;
using Databy.Reports.Catalog;
using Databy.Reports.Export;
using ColumnDefinition = Databy.Reports.Catalog.ColumnDefinition;

namespace Tests.Unit;

[Trait("Category", "T1")]
public sealed class ResultColumnsTests
{
    private static readonly string[] DatabaseColumns =
        ["ОГРН", "ИНН", "Наименование организации", "name_", "name_full_"];

    [Fact]
    public void Rc01_WithoutResultAllDatabaseColumnsAreVisibleInDatabaseOrder()
    {
        var columns = ResultColumns.Resolve(DatabaseColumns, null);

        Assert.Equal(DatabaseColumns, columns.Select(column => column.Name));
        Assert.All(columns, column => Assert.Equal(column.Name, column.Title));
        Assert.All(columns, column => Assert.Null(column.Format));
    }

    [Fact]
    public void Rc02_HideRemovesOnlyNamedColumnsAndKeepsOrder()
    {
        var result = new ResultDefinition { Hide = ["ОГРН"] };

        var columns = ResultColumns.Resolve(DatabaseColumns, result);

        Assert.Equal(["ИНН", "Наименование организации", "name_", "name_full_"], columns.Select(column => column.Name));
    }

    [Fact]
    public void Rc03_HiddenFlagIsEquivalentToHide()
    {
        var byFlag = ResultColumns.Resolve(DatabaseColumns, new ResultDefinition
        {
            Columns = new Dictionary<string, ColumnDefinition>(StringComparer.Ordinal) { ["ИНН"] = new() { Hidden = true } }
        });
        var byHide = ResultColumns.Resolve(DatabaseColumns, new ResultDefinition { Hide = ["ИНН"] });

        Assert.Equal(byHide.Select(column => column.Name), byFlag.Select(column => column.Name));
    }

    [Fact]
    public void Rc04_DescribedColumnGetsTitleAndFormatOthersUseNames()
    {
        var result = new ResultDefinition
        {
            Columns = new Dictionary<string, ColumnDefinition>(StringComparer.Ordinal)
            {
                ["name_"] = new() { Title = "Краткое наименование", Format = "N0" }
            }
        };

        var columns = ResultColumns.Resolve(DatabaseColumns, result);

        var described = columns.Single(column => column.Name == "name_");
        Assert.Equal("Краткое наименование", described.Title);
        Assert.Equal("N0", described.Format);
        Assert.Equal("ОГРН", columns[0].Title);
        Assert.Null(columns[0].Format);
    }

    [Fact]
    public void Rc05_CyrillicNameWithSpacesIsHiddenPrecisely()
    {
        var result = new ResultDefinition { Hide = ["Наименование организации"] };

        var columns = ResultColumns.Resolve(DatabaseColumns, result);

        Assert.DoesNotContain(columns, column => column.Name == "Наименование организации");
        Assert.Equal(4, columns.Count);
    }

    [Fact]
    public void Rc06_NamesAreCaseSensitive()
    {
        var result = new ResultDefinition { Hide = ["огрн"] };

        var columns = ResultColumns.Resolve(DatabaseColumns, result);

        Assert.Contains(columns, column => column.Name == "ОГРН");
        _ = ResultColumns.Resolve(DatabaseColumns, result, out var unknown);
        Assert.Equal(["огрн"], unknown);
    }

    [Fact]
    public void Rc07_UnknownHiddenNamesAreReportedButColumnsStayVisible()
    {
        var result = new ResultDefinition { Hide = ["missing_column", "ОГРН"] };

        var columns = ResultColumns.Resolve(DatabaseColumns, result, out var unknown);

        Assert.Equal(["missing_column"], unknown);
        Assert.Equal(4, columns.Count);
    }

    [Fact]
    public void Rc08_HideAndHiddenFalseConflictIsCatalogError()
    {
        using var dir = new TempDirectory();
        CatalogTests.Write(dir.Path, "bad.yml", ReportWithResult("""
                hide: [a]
                columns:
                  a: { hidden: false }
            """));

        var catalog = new ReportCatalogLoader().Load(dir.Path);

        Assert.Empty(catalog.Reports);
        var message = Assert.Single(catalog.Errors).Message;
        Assert.Contains("a", message);
        Assert.Contains("hidden: false", message);
    }

    [Fact]
    public void Rc09_DuplicateOrEmptyHideEntriesAreCatalogErrors()
    {
        using var duplicateDir = new TempDirectory();
        CatalogTests.Write(duplicateDir.Path, "dup.yml", ReportWithResult("hide: [a, a]"));
        var duplicate = new ReportCatalogLoader().Load(duplicateDir.Path);
        Assert.Contains("повторяется", Assert.Single(duplicate.Errors).Message, StringComparison.OrdinalIgnoreCase);

        using var emptyDir = new TempDirectory();
        CatalogTests.Write(emptyDir.Path, "empty.yml", ReportWithResult("hide: [\"\"]"));
        var empty = new ReportCatalogLoader().Load(emptyDir.Path);
        Assert.Contains("пустое имя", Assert.Single(empty.Errors).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rc10_InvalidHideKeepsOtherReportsLoadable()
    {
        using var dir = new TempDirectory();
        CatalogTests.Write(dir.Path, "good.yml", CatalogTests.Valid("good"));
        CatalogTests.Write(dir.Path, "bad.yml", ReportWithResult("hide: [x, x]"));

        var catalog = new ReportCatalogLoader().Load(dir.Path);

        Assert.Equal("good", Assert.Single(catalog.Reports).Code);
        Assert.Single(catalog.Errors);
    }

    [Fact]
    public void Rc11_AllColumnsHiddenFailsExportWithoutCreatingFile()
    {
        using var files = new TempDirectory();
        var table = Data(["a", "b"]);
        var stage = Stage(files.Path, table);
        var path = Path.Combine(files.Path, "hidden.csv");
        var result = new ResultDefinition { Hide = ["a", "b"] };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ReportExportWriter().Save(stage, table, path, null, result, table.Rows.Count));

        Assert.Contains("Все колонки скрыты", ex.Message);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Rc12_TableAndExportsAgreeOnColumns()
    {
        using var files = new TempDirectory();
        var table = Data(["a", "b", "c"]);
        var stage = Stage(files.Path, table);
        var result = new ResultDefinition
        {
            Hide = ["b"],
            Columns = new Dictionary<string, ColumnDefinition>(StringComparer.Ordinal) { ["c"] = new() { Title = "Колонка C" } }
        };

        var databaseColumns = table.Columns.Cast<DataColumn>().Select(column => column.ColumnName);
        var expected = ResultColumns.Resolve(databaseColumns, result).Select(column => column.Title).ToArray();

        var xlsxPath = Path.Combine(files.Path, "result.xlsx");
        new ReportExportWriter().Save(stage, table, xlsxPath, "Rows", result, table.Rows.Count);
        var xlsxHeaders = ReadXlsxHeaders(xlsxPath);

        var csvPath = Path.Combine(files.Path, "result.csv");
        new ReportExportWriter().Save(stage, table, csvPath, null, result, table.Rows.Count);
        var csvHeaders = ReadCsvHeader(csvPath);

        Assert.Equal(expected, xlsxHeaders);
        Assert.Equal(expected, csvHeaders);
        Assert.Equal(["a", "Колонка C"], expected);
    }

    private static string[] ReadXlsxHeaders(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var reader = ExcelDataReader.ExcelReaderFactory.CreateReader(File.OpenRead(path));
        Assert.True(reader.Read());
        return Enumerable.Range(0, reader.FieldCount).Select(reader.GetString).ToArray();
    }

    private static string[] ReadCsvHeader(string path)
    {
        using var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(path, Encoding.UTF8)
        {
            TextFieldType = Microsoft.VisualBasic.FileIO.FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(";");
        return parser.ReadFields()!;
    }

    private static DataTable Data(params string[] names)
    {
        var table = new DataTable();
        foreach (var name in names) table.Columns.Add(name, typeof(string));
        table.Rows.Add(names.Select(_ => (object)"value").ToArray());
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

    private static string ReportWithResult(string resultBody)
    {
        var indented = string.Join(Environment.NewLine,
            resultBody.Split('\n').Select(line => "  " + line.TrimEnd('\r')));
        return $"""
            code: test.report
            title: Отчёт
            routine:
              name: reports.fn_test
              kind: function
            parameters: []
            result:
            {indented}
            roles: [admin, user]
            """;
    }
}
