using Databy.Reports.Catalog;

namespace Tests.Unit;

[Trait("Category", "T1")]
public sealed class InputDefinitionTests
{
    [Fact]
    public void In01_MinimalValidInputIsLoaded()
    {
        using var dir = new TempDirectory();
        CatalogTests.Write(dir.Path, "org.yml", Report("""
              row_id_parameter: p_row_id
              result_key: row_id
              columns:
                - { name: p_inn, type: string, label: ИНН, required: true }
            """));

        var catalog = new ReportCatalogLoader().Load(dir.Path);

        Assert.Empty(catalog.Errors);
        var input = Assert.Single(catalog.Reports).Input;
        Assert.NotNull(input);
        Assert.Equal("p_row_id", input!.RowIdParameter);
        Assert.Equal("row_id", input.ResultKey);
        Assert.Equal(["p_inn"], input.Columns.Select(column => column.Name));
        Assert.True(input.Columns[0].Required);
    }

    [Fact]
    public void In02_DefaultsAreApplied()
    {
        using var dir = new TempDirectory();
        CatalogTests.Write(dir.Path, "org.yml", Report("""
              row_id_parameter: p_row_id
              result_key: row_id
              columns:
                - { name: p_inn, type: string }
            """));

        var input = Assert.Single(new ReportCatalogLoader().Load(dir.Path).Reports).Input!;

        Assert.Equal("Список значений", input.Label);
        Assert.Equal(1_000_000, input.MaxRows);
        Assert.Equal(100_000, input.BatchSize);
        Assert.Equal("Статус поиска", input.StatusColumn);
        Assert.True(input.Columns[0].Required);
    }

    [Fact]
    public void In03_RequiredCanBeSetToFalse()
    {
        using var dir = new TempDirectory();
        CatalogTests.Write(dir.Path, "org.yml", Report("""
              row_id_parameter: p_row_id
              result_key: row_id
              columns:
                - { name: p_inn, type: string, required: false }
            """));

        var input = Assert.Single(new ReportCatalogLoader().Load(dir.Path).Reports).Input!;

        Assert.False(input.Columns[0].Required);
    }

    [Fact]
    public void In04_ReportWithoutInputStaysOrdinary()
    {
        using var dir = new TempDirectory();
        CatalogTests.Write(dir.Path, "plain.yml", CatalogTests.Valid("plain"));

        var report = Assert.Single(new ReportCatalogLoader().Load(dir.Path).Reports);

        Assert.Null(report.Input);
    }

    public static IEnumerable<object[]> InvalidInputs() =>
    [
        ["no-columns", """
              row_id_parameter: p_row_id
              result_key: row_id
              columns: []
            """, "хотя бы одна колонка"],
        ["bad-column-name", """
              row_id_parameter: p_row_id
              result_key: row_id
              columns:
                - { name: p-bad, type: string }
            """, "Недопустимое имя колонки"],
        ["bad-row-id", """
              row_id_parameter: p-bad
              result_key: row_id
              columns:
                - { name: p_inn, type: string }
            """, "input.row_id_parameter"],
        ["duplicate-column", """
              row_id_parameter: p_row_id
              result_key: row_id
              columns:
                - { name: p_inn, type: string }
                - { name: p_inn, type: int }
            """, "Повторяется имя колонки"],
        ["column-equals-parameter", """
              row_id_parameter: p_row_id
              result_key: row_id
              columns:
                - { name: p_date, type: string }
            """, "совпадает с именем параметра"],
        ["empty-result-key", """
              row_id_parameter: p_row_id
              result_key: ""
              columns:
                - { name: p_inn, type: string }
            """, "result_key"],
        ["status-equals-result", """
              row_id_parameter: p_row_id
              result_key: row_id
              status_column: row_id
              columns:
                - { name: p_inn, type: string }
            """, "не должен совпадать"],
        ["bad-column-type", """
              row_id_parameter: p_row_id
              result_key: row_id
              columns:
                - { name: p_inn, type: nonsense }
            """, "Неизвестный тип колонки"],
        ["batch-greater-max", """
              max_rows: 100
              batch_size: 200
              row_id_parameter: p_row_id
              result_key: row_id
              columns:
                - { name: p_inn, type: string }
            """, "batch_size"],
        ["max-rows-zero", """
              max_rows: 0
              row_id_parameter: p_row_id
              result_key: row_id
              columns:
                - { name: p_inn, type: string }
            """, "max_rows"],
        ["max-rows-too-big", """
              max_rows: 2000000
              row_id_parameter: p_row_id
              result_key: row_id
              columns:
                - { name: p_inn, type: string }
            """, "max_rows"],
        ["procedure-kind", """
              row_id_parameter: p_row_id
              result_key: row_id
              columns:
                - { name: p_inn, type: string }
            """, "routine.kind"]
    ];

    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public void In05_InvalidInputProducesFileErrorAndKeepsOtherReports(string name, string inputBody, string expected)
    {
        using var dir = new TempDirectory();
        CatalogTests.Write(dir.Path, "good.yml", CatalogTests.Valid("good"));
        CatalogTests.Write(dir.Path, "bad.yml", Report(inputBody, kind: name == "procedure-kind" ? "procedure" : "function"));

        var catalog = new ReportCatalogLoader().Load(dir.Path);

        Assert.True(catalog.Errors.Count == 1, $"'{name}': ожидалась одна ошибка, получено {catalog.Errors.Count}");
        Assert.Equal("good", Assert.Single(catalog.Reports).Code);
        var error = Assert.Single(catalog.Errors);
        Assert.EndsWith("bad.yml", error.File);
        Assert.Contains(expected, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void In06_RealCatalogStillPassesLint()
    {
        var catalog = new ReportCatalogLoader().Load(RepositoryCatalog());
        Assert.Empty(catalog.Errors);
        Assert.DoesNotContain(catalog.Reports, report => report.Input is not null);
    }

    private static string Report(string input, string kind = "function") => $"""
        code: org.by_inn
        title: Организации по списку ИНН
        routine:
          name: reports.fn_orgs_by_inn
          kind: {kind}
          timeout_sec: 600
        parameters:
          - name: p_date
            type: date
            label: Дата
            required: true
            default: today
        input:
        {Indent(input)}
        roles: [admin, user]
        """;

    private static string Indent(string value) =>
        string.Join(Environment.NewLine, value.Split('\n').Select(line => "  " + line.TrimEnd('\r')));

    private static string RepositoryCatalog()
    {
        var path = new DirectoryInfo(AppContext.BaseDirectory);
        while (path is not null)
        {
            var candidate = Path.Combine(path.FullName, "catalog");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "catalog.version"))) return candidate;
            path = path.Parent;
        }
        throw new DirectoryNotFoundException("catalog не найден");
    }
}
