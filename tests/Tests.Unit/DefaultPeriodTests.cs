using System.Data;
using Databy.Reports.Catalog;
using Databy.Reports.Export;
using Databy.Reports.Services;
using Databy.Reports.ViewModels;

namespace Tests.Unit;

[Trait("Category", "T1")]
public sealed class DefaultPeriodTests
{
    [Fact]
    public void Dp01_PeriodUsesReportDefaultParameters()
    {
        using var viewModel = CreateViewModel(new ParameterDefinition { Name = "p_from", Type = "date", Default = "2026-01-01" },
            new ParameterDefinition { Name = "p_to", Type = "date", Default = "2026-03-31" });
        OpenFirstReport(viewModel);

        Assert.Equal(new DateTimeOffset(new DateTime(2026, 1, 1)), viewModel.FromDate);
        Assert.Equal(new DateTimeOffset(new DateTime(2026, 3, 31)), viewModel.ToDate);
        Assert.Equal(viewModel.FromDate, viewModel.FilterFromDate);
        Assert.Equal(viewModel.ToDate, viewModel.FilterToDate);
    }

    [Fact]
    public void Dp02_PeriodFallsBackToCurrentMonthWhenNoDefaults()
    {
        using var viewModel = CreateViewModel(new ParameterDefinition { Name = "p_from", Type = "date" },
            new ParameterDefinition { Name = "p_to", Type = "date" });
        OpenFirstReport(viewModel);

        var today = DateTime.Today;
        Assert.Equal(new DateTimeOffset(new DateTime(today.Year, today.Month, 1)), viewModel.FromDate);
        Assert.Equal(today, viewModel.ToDate!.Value.Date);
    }

    [Fact]
    public async Task Dp03_RunSendsSelectedPeriodToQuery()
    {
        IReadOnlyDictionary<string, object?>? received = null;
        using var viewModel = CreateViewModel(
            new ParameterDefinition { Name = "p_from", Type = "date" },
            new ParameterDefinition { Name = "p_to", Type = "date" },
            filter => received = filter);
        OpenFirstReport(viewModel);

        viewModel.SetPeriod(new DateTimeOffset(new DateTime(2026, 10, 1)), new DateTimeOffset(new DateTime(2026, 10, 31)));
        await viewModel.RunCommand.ExecuteAsync(null);

        Assert.NotNull(received);
        Assert.Equal(new DateOnly(2026, 10, 1), received!["p_from"]);
        Assert.Equal(new DateOnly(2026, 10, 31), received["p_to"]);
        Assert.Single(viewModel.SelectedTab!.TableRows, row => !row.IsHeader);
        Assert.Equal(1, viewModel.SelectedTab!.PreviewRows);
    }

    [Fact]
    public async Task Dp04_TypedDefaultsAreSentForNonDateParameters()
    {
        IReadOnlyDictionary<string, object?>? received = null;
        using var viewModel = CreateFor(
            [
                new ParameterDefinition { Name = "p_limit", Type = "int", Required = true, Default = "5" },
                new ParameterDefinition { Name = "p_region", Type = "string" }
            ],
            parameters => received = parameters);
        OpenFirstReport(viewModel);

        await viewModel.RunCommand.ExecuteAsync(null);

        Assert.NotNull(received);
        Assert.Equal(5, received!["p_limit"]);
        Assert.IsType<int>(received["p_limit"]);
        Assert.False(received.ContainsKey("p_region") && received["p_region"] is not null);
    }

    [Fact]
    public async Task Dp05_RequiredIntWithoutDefaultProducesParameterError()
    {
        using var viewModel = CreateFor([new ParameterDefinition { Name = "p_limit", Type = "int", Required = true }]);
        OpenFirstReport(viewModel);

        await viewModel.RunCommand.ExecuteAsync(null);

        Assert.Contains("Ошибка отчёта", viewModel.Status);
        Assert.Contains("p_limit", viewModel.Status);
    }

    private static MainWindowViewModel CreateFor(ParameterDefinition[] parameters, Action<IReadOnlyDictionary<string, object?>>? onQuery = null)
    {
        var definition = new ReportDefinition
        {
            Code = "sales.top_products",
            Title = "Топ товаров",
            Routine = new RoutineDefinition { Name = "reports.fn_top_products" },
            Parameters = [.. parameters],
            Result = new ResultDefinition { Columns = new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase) { ["amount"] = new() { Title = "Сумма" } } }
        };
        return new MainWindowViewModel(new StubCatalog(definition), new StubQuery(onQuery), new StubExport())
        {
            MinimumLoadingTime = TimeSpan.Zero
        };
    }

    private static void OpenFirstReport(MainWindowViewModel viewModel) =>
        viewModel.OpenReport(viewModel.Catalog.SelectMany(folder => folder.Children).First(node => node.Report is not null).Report!);

    private static MainWindowViewModel CreateViewModel(ParameterDefinition from, ParameterDefinition to, Action<IReadOnlyDictionary<string, object?>>? onQuery = null)
    {
        var definition = new ReportDefinition
        {
            Code = "sales.monthly",
            Title = "Продажи",
            Routine = new RoutineDefinition { Name = "reports.fn_sales" },
            Parameters = [from, to],
            Result = new ResultDefinition { Columns = new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase) { ["amount"] = new() { Title = "Сумма" } } }
        };
        return new MainWindowViewModel(new StubCatalog(definition), new StubQuery(onQuery), new StubExport())
        {
            MinimumLoadingTime = TimeSpan.Zero
        };
    }

    private sealed class StubCatalog(ReportDefinition report) : IReportCatalogService
    {
        public ReportCatalog Load() => new("1.0", [report], [], ["sales"]);
    }

    private sealed class StubQuery(Action<IReadOnlyDictionary<string, object?>>? onQuery) : IReportQueryService
    {
        public Task<ReportQueryResult> ExecuteAsync(ReportDefinition report, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
        {
            onQuery?.Invoke(parameters);
            var table = new DataTable();
            table.Columns.Add("amount", typeof(decimal));
            table.Rows.Add(1m);
            return Task.FromResult(new ReportQueryResult(new ReportPreview(table, 1), new TemporaryCsvStage(), 1));
        }
    }

    private sealed class StubExport : IReportExportService
    {
        public ExportResult Save(ReportQueryResult result, ReportDefinition report, string targetPath) =>
            new(targetPath, "CSV", result.RowCount, false);
    }
}