using System.Data;
using Databy.Reports.Catalog;
using Databy.Reports.Export;
using Databy.Reports.Services;
using Databy.Reports.ViewModels;

namespace Tests.Unit;

[Trait("Category", "T1")]
public sealed class ParameterPanelTests
{
    [Fact]
    public void Pp01_ReportWithoutParametersHidesApplyAndShowsMessage()
    {
        using var viewModel = Create([]);

        Assert.True(viewModel.IsReportSelected);
        Assert.False(viewModel.HasReportParameters);
        Assert.False(viewModel.HasDateParameters);
        Assert.True(viewModel.HasNoParameters);
    }

    [Fact]
    public void Pp02_ReportWithDateParametersShowsDateFilterAndApply()
    {
        using var viewModel = Create(
        [
            new ParameterDefinition { Name = "p_from", Type = "date" },
            new ParameterDefinition { Name = "p_to", Type = "date" }
        ]);

        Assert.True(viewModel.HasReportParameters);
        Assert.True(viewModel.HasDateParameters);
        Assert.False(viewModel.HasNoParameters);
    }

    [Fact]
    public void Pp03_ReportWithOnlyNonDateParametersHidesDateFilterButKeepsApply()
    {
        using var viewModel = Create([new ParameterDefinition { Name = "p_limit", Type = "int", Default = "5" }]);

        Assert.True(viewModel.HasReportParameters);
        Assert.False(viewModel.HasDateParameters);
        Assert.False(viewModel.HasNoParameters);
    }

    [Fact]
    public void Pp04_SwitchingTabsUpdatesParameterState()
    {
        using var viewModel = new MainWindowViewModel(
            new StubCatalog(
                Definition("test.none", []),
                Definition("test.dates", [new ParameterDefinition { Name = "p_from", Type = "date" }])),
            new StubQuery(), new StubExport())
        {
            MinimumLoadingTime = TimeSpan.Zero
        };

        viewModel.OpenReport(viewModel.Catalog[0].Children[0].Report!);
        Assert.True(viewModel.HasNoParameters);

        viewModel.OpenReport(viewModel.Catalog[0].Children[1].Report!);
        Assert.False(viewModel.HasNoParameters);
        Assert.True(viewModel.HasDateParameters);
    }

    private static MainWindowViewModel Create(ParameterDefinition[] parameters)
    {
        var viewModel = new MainWindowViewModel(
            new StubCatalog(Definition("test.report", parameters)), new StubQuery(), new StubExport())
        {
            MinimumLoadingTime = TimeSpan.Zero
        };
        viewModel.OpenReport(viewModel.Catalog[0].Children[0].Report!);
        return viewModel;
    }

    private static ReportDefinition Definition(string code, ParameterDefinition[] parameters) => new()
    {
        Code = code,
        Title = code,
        Routine = new RoutineDefinition { Name = "reports.fn_test" },
        Parameters = [.. parameters],
        Result = new ResultDefinition { Columns = new Dictionary<string, ColumnDefinition> { ["amount"] = new() { Title = "Сумма" } } }
    };

    private sealed class StubCatalog(params ReportDefinition[] definitions) : IReportCatalogService
    {
        public ReportCatalog Load() => new("1", definitions, [], ["test"]);
    }

    private sealed class StubQuery : IReportQueryService
    {
        public Task<ReportQueryResult> ExecuteAsync(ReportDefinition report, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
        {
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
