using Databy.Reports.Catalog;
using Databy.Reports.Input;
using Databy.Reports.Services;
using Xunit;

namespace Tests.Integration;

/// <summary>Пример конфига catalog/sales/sales_by_cities.yml: два города из списка.</summary>
[Trait("Category", "T2")]
[Collection(PostgresCollection.Name)]
public sealed class CitiesExampleTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Example_TwoCitiesReturnSales()
    {
        var parse = new ListInputParser().ParseText("Москва\nКазань\n", new InputParseOptions
        {
            Delimiter = InputDelimiter.None,
            Columns = [new InputColumnDefinition { Name = "p_city", Type = "string" }],
            ColumnMapping = new Dictionary<string, int> { ["p_city"] = 0 }
        });

        var report = LoadExample();
        var service = new ListReportQueryService(fixture.Settings);
        using var input = parse;

        using var result = await service.ExecuteAsync(report,
            new Dictionary<string, object?>
            {
                ["p_from"] = new DateOnly(2026, 10, 1),
                ["p_to"] = new DateOnly(2026, 10, 31)
            },
            parse.Stage, null, CancellationToken.None);

        Assert.True(result.RowCount > 0);
        var lines = File.ReadAllLines(result.Stage.Path, System.Text.Encoding.UTF8);
        Assert.Contains(lines, line => line.Contains("Москва"));
        Assert.Contains(lines, line => line.Contains("Казань"));

        // Превью для таблицы должно содержать столько же строк, сколько вернула БД.
        Assert.Equal(result.RowCount, result.Preview.Table.Rows.Count);
        Assert.Equal(result.RowCount, result.Preview.TotalRows);
    }

    private ReportDefinition LoadExample()
    {
        var directory = Path.Combine(fixture.TempDirectory, "cities-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var source = Path.Combine(AppContext.BaseDirectory, "example-catalog", "sales", "sales_by_cities.yml");
        var target = Path.Combine(directory, "sales_by_cities.yml");
        File.Copy(source, target, true);
        var catalog = new ReportCatalogLoader().Load(directory);
        Assert.Empty(catalog.Errors);
        return Assert.Single(catalog.Reports);
    }
}
