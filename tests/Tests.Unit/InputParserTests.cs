using System.Text;
using Databy.Reports.Catalog;
using Databy.Reports.Input;

namespace Tests.Unit;

[Trait("Category", "T1")]
[Collection("temp-stage")]
public sealed class InputParserTests
{
    private static InputParseOptions Options(bool hasHeader = false, params string[] columns) => new()
    {
        HasHeader = hasHeader,
        Columns = Columns(columns)
    };

    private static InputColumnDefinition[] Columns(params string[] specs) =>
        specs.Select(spec =>
        {
            var parts = spec.Split(':');
            return new InputColumnDefinition { Name = parts[0], Type = parts[1], Required = parts.Length < 3 || parts[2] == "required" };
        }).ToArray();

    [Fact]
    public void Ip01_ExcelPasteUsesTabsAndCrLf()
    {
        var text = "111\t2026-01-15\r\n222\t2026-02-20\r\n";
        using var result = new ListInputParser().ParseText(text, new InputParseOptions
        {
            Columns = Columns("p_inn:string", "p_date:date"),
            ColumnMapping = new Dictionary<string, int> { ["p_inn"] = 0, ["p_date"] = 1 }
        });

        Assert.Equal(2, result.TotalRows);
        Assert.Equal(2, result.ValidRows);
        Assert.Equal(0, result.ErrorRows);
        Assert.Equal(["Колонка 1", "Колонка 2"], result.SourceColumns);
    }

    [Fact]
    public void Ip02_CsvWithSemicolonAndComma()
    {
        using var semicolon = new ListInputParser().ParseText("a;b\r\n1;2\r\n", Options());
        Assert.Equal(2, semicolon.SourceColumns.Count);
        Assert.Equal(2, semicolon.TotalRows);

        using var comma = new ListInputParser().ParseText("1,2\n3,4\n", new InputParseOptions { Delimiter = InputDelimiter.Comma });
        Assert.Equal(2, comma.SourceColumns.Count);
        Assert.Equal(2, comma.TotalRows);
    }

    [Fact]
    public void Ip03_SingleColumnWithoutDelimiter()
    {
        using var result = new ListInputParser().ParseText("111\n222\n333", Options());

        Assert.Single(result.SourceColumns);
        Assert.Equal(3, result.TotalRows);
        var rows = result.Stage.ReadRows().ToArray();
        Assert.Equal(["111", "222", "333"], rows.Select(row => row.Cells[0]));
    }

    [Fact]
    public void Ip04_QuotesAndEmbeddedNewlines()
    {
        var text = "name;note\n\"Иванов; И.И.\";\"строка 1\nстрока 2\"\n";
        using var result = new ListInputParser().ParseText(text, new InputParseOptions { Delimiter = InputDelimiter.Semicolon, HasHeader = true });

        Assert.Equal(1, result.TotalRows);
        var row = Assert.Single(result.Stage.ReadRows());
        Assert.Equal("Иванов; И.И.", row.Cells[0]);
        Assert.Equal("строка 1\nстрока 2", row.Cells[1]);
    }

    [Fact]
    public void Ip05_Utf8BomAndWindows1251Files()
    {
        using var dir = new TempDirectory();

        var utf8 = Path.Combine(dir.Path, "utf8.csv");
        File.WriteAllText(utf8, "ИНН;Имя\n111;Иванов\n", new UTF8Encoding(true));

        var win1251 = Path.Combine(dir.Path, "win1251.csv");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        File.WriteAllBytes(win1251, Encoding.GetEncoding(1251).GetBytes("ИНН;Имя\n111;Иванов\n"));

        foreach (var path in new[] { utf8, win1251 })
        {
            using var result = new ListInputParser().ParseFile(path, new InputParseOptions { Delimiter = InputDelimiter.Semicolon, HasHeader = true });
            var row = Assert.Single(result.Stage.ReadRows());
            Assert.Equal("Иванов", row.Cells[1]);
            Assert.Equal(["ИНН", "Имя"], result.SourceColumns);
        }
    }

    [Fact]
    public void Ip06_EmptyRowsAreSkipped()
    {
        using var result = new ListInputParser().ParseText("a;b\n\n1;2\n   \n\n3;4\n", new InputParseOptions { Delimiter = InputDelimiter.Semicolon, HasHeader = true });

        Assert.Equal(2, result.TotalRows);
        var rows = result.Stage.ReadRows().ToArray();
        Assert.Equal([1, 2], rows.Select(row => row.RowId));
    }

    [Fact]
    public void Ip07_HeaderIsUsedAndDuplicatesGetSuffixes()
    {
        using var result = new ListInputParser().ParseText("ИНН;ИНН;Имя\n1;2;Иванов\n",
            new InputParseOptions { Delimiter = InputDelimiter.Semicolon, HasHeader = true });

        Assert.Equal(["ИНН", "ИНН (2)", "Имя"], result.SourceColumns);
        Assert.Equal(1, result.TotalRows);
    }

    [Fact]
    public void Ip08_TypeErrorsKeepRowAndDoNotLeakValue()
    {
        var text = "111;2026-13-40\n\n222;\n\n333;2026-01-01\n";
        using var result = new ListInputParser().ParseText(text, new InputParseOptions
        {
            Delimiter = InputDelimiter.Semicolon,
            Columns = Columns("p_inn:string", "p_date:date"),
            ColumnMapping = new Dictionary<string, int> { ["p_inn"] = 0, ["p_date"] = 1 }
        });

        Assert.Equal(3, result.TotalRows);
        Assert.Equal(1, result.ValidRows);
        Assert.Equal(2, result.ErrorRows);
        Assert.Equal(3, result.Stage.ReadRows().Count());
        Assert.All(result.Errors, error => Assert.DoesNotContain("2026-13-40", error.Reason));
        Assert.Contains(result.Errors, error => error.Column == "p_date" && error.RowId == 1);
        Assert.Contains(result.Errors, error => error.RowId == 2);
    }

    [Fact]
    public void Ip09_IntOutOfRangeIsError()
    {
        using var result = new ListInputParser().ParseText("99999999999\n", new InputParseOptions
        {
            Columns = Columns("p_inn:int"),
            ColumnMapping = new Dictionary<string, int> { ["p_inn"] = 0 }
        });

        Assert.Equal(1, result.ErrorRows);
        Assert.Equal("p_inn", Assert.Single(result.Errors).Column);
    }

    [Fact]
    public void Ip10_MaxRowsExceededThrowsAndDeletesStage()
    {
        var before = CountStages();
        var parser = new ListInputParser();
        var text = string.Join('\n', Enumerable.Range(1, 10).Select(i => i.ToString()));

        var ex = Assert.Throws<InvalidOperationException>(() => parser.ParseText(text, new InputParseOptions { MaxRows = 5 }));

        Assert.Contains("Превышен лимит", ex.Message);
        Assert.True(CountStages() <= before, "Stage должен быть удалён при превышении лимита");
    }

    [Fact]
    public void Ip11_CancellationRemovesStage()
    {
        var before = CountStages();
        using var cts = new CancellationTokenSource();
        var text = string.Join('\n', Enumerable.Range(1, 5000).Select(i => i.ToString()));
        var progress = new InlineProgress<long>(_ => cts.Cancel());

        Assert.ThrowsAny<OperationCanceledException>(() =>
            new ListInputParser().ParseText(text, new InputParseOptions(), cts.Token, progress));
        Assert.True(CountStages() <= before, "Stage должен быть удалён при отмене");
    }

    private static int CountStages() =>
        Directory.GetFiles(Path.GetTempPath(), "databy-input-*.csv").Length;

    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    [Fact]
    public void Ip12_PreviewIsLimitedTo100Rows()
    {
        var text = string.Join('\n', Enumerable.Range(1, 250).Select(i => i.ToString()));
        using var result = new ListInputParser().ParseText(text, new InputParseOptions());

        Assert.Equal(250, result.TotalRows);
        Assert.Equal(100, result.Preview.Count);
        Assert.Equal(1, result.Preview[0].RowId);
        Assert.Equal(100, result.Preview[^1].RowId);
    }

    [Fact]
    public void Ip13_ErrorsAreLimitedTo100()
    {
        var text = string.Join('\n', Enumerable.Range(1, 250).Select(_ => "not-a-date"));
        using var result = new ListInputParser().ParseText(text, new InputParseOptions
        {
            Columns = Columns("p_date:date"),
            ColumnMapping = new Dictionary<string, int> { ["p_date"] = 0 }
        });

        Assert.Equal(250, result.ErrorRows);
        Assert.Equal(100, result.Errors.Count);
    }

    [Fact]
    public void Ip14_OptionalAndStringValuesPreserveWhitespace()
    {
        using var result = new ListInputParser().ParseText(" 111 ;  текст  \n\n",
            new InputParseOptions
            {
                Delimiter = InputDelimiter.Semicolon,
                Columns = Columns("p_inn:string", "p_note:string:optional"),
                ColumnMapping = new Dictionary<string, int> { ["p_inn"] = 0, ["p_note"] = 1 }
            });

        var row = Assert.Single(result.Stage.ReadRows());
        Assert.Equal(" 111 ", row.Cells[0]);
        Assert.Equal("  текст  ", row.Cells[1]);
        Assert.Equal(0, result.ErrorRows);
    }
}
