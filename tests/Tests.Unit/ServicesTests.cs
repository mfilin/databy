using System.Data;
using Databy.Reports.Catalog;
using Databy.Reports.Export;
using Databy.Reports.Services;
using Databy.Reports.ViewModels;

namespace Tests.Unit;

[Trait("Category", "T1")]
public sealed class MainWindowViewModelTests
{
    

    [Fact]
    public void Vm01_BuildsCatalogTreeWithoutAutoOpeningReport()
    {
        var viewModel = CreateViewModel();
        try
        {
            var folder = Assert.Single(viewModel.Catalog);
            Assert.Equal("sales", folder.Title);
            Assert.True(folder.IsFolder);
            var reportNode = Assert.Single(folder.Children);
            Assert.False(reportNode.IsFolder);
            Assert.NotNull(reportNode.Report);

            Assert.Null(viewModel.SelectedTab);
            Assert.Empty(viewModel.Tabs);
            Assert.False(viewModel.IsReportSelected);
            Assert.Equal("2.0", viewModel.CatalogVersion);
            Assert.False(viewModel.CanExport);
        }
        finally { viewModel.Dispose(); }
    }

    [Fact]
    public void Vm02_OpenReportReusesExistingTab()
    {
        var viewModel = CreateViewModel();
        try
        {
            var definition = FirstReport(viewModel);
            var first = viewModel.OpenReport(definition);
            Assert.True(viewModel.IsReportSelected);
            var second = viewModel.OpenReport(definition);
            Assert.Same(first, second);
            Assert.Single(viewModel.Tabs);
        }
        finally { viewModel.Dispose(); }
    }

    [Fact]
    public async Task Vm03_RunFillsRowsAndEnablesPaging()
    {
        var viewModel = CreateViewModel();
        try
        {
            viewModel.OpenReport(FirstReport(viewModel));
            await viewModel.RunCommand.ExecuteAsync(null);
            Assert.True(viewModel.HasData);
            Assert.False(viewModel.CanGoForward);
            Assert.False(viewModel.CanGoBack);
            Assert.Equal("1 из 1", viewModel.PageCaption);
            Assert.True(viewModel.CanExport);
            Assert.Equal(2, viewModel.SelectedTab!.PreviewRows);
        }
        finally { viewModel.Dispose(); }
    }

    [Fact]
    public async Task Vm04_PaginationSlicesPreviewByPageSize()
    {
        var viewModel = CreateViewModel(rowCount: MainWindowViewModel.PageSize * 2 + 5);
        try
        {
            viewModel.OpenReport(FirstReport(viewModel));
            await viewModel.RunCommand.ExecuteAsync(null);
            Assert.Equal(3, viewModel.PageCount);
            Assert.True(viewModel.CanGoForward);

            viewModel.NextPageCommand.Execute(null);
            Assert.Equal(1, viewModel.Page);
            Assert.True(viewModel.CanGoBack);

            viewModel.LastPageCommand.Execute(null);
            Assert.Equal(2, viewModel.Page);
            Assert.Equal(5, viewModel.SelectedTab!.Rows!.Count);
            Assert.False(viewModel.CanGoForward);

            viewModel.FirstPageCommand.Execute(null);
            Assert.Equal(0, viewModel.Page);
            Assert.Equal(MainWindowViewModel.PageSize, viewModel.SelectedTab!.Rows!.Count);
        }
        finally { viewModel.Dispose(); }
    }

    [Fact]
    public async Task Vm05_InvalidPeriodProducesErrorStatus()
    {
        var viewModel = CreateViewModel();
        try
        {
            viewModel.OpenReport(FirstReport(viewModel));
            viewModel.SetPeriod(new DateTimeOffset(new DateTime(2026, 5, 10)), new DateTimeOffset(new DateTime(2026, 5, 1)));
            await viewModel.RunCommand.ExecuteAsync(null);
            Assert.Contains("не может быть раньше", viewModel.Status);
            Assert.False(viewModel.HasData);
        }
        finally { viewModel.Dispose(); }
    }

    [Fact]
    public void Vm06_CatalogFoldersFromYamlAreUsedForTree()
    {
        var viewModel = CreateViewModel();
        try
        {
            Assert.Equal("sales", Assert.Single(viewModel.Catalog).Title);
            Assert.Equal("sales.monthly", viewModel.Catalog[0].Children[0].ReportCode);
        }
        finally { viewModel.Dispose(); }
    }

    [Fact]
    public void Vm07_CloseTabSelectsNeighbourAndClearsSelectionWhenEmpty()
    {
        var viewModel = CreateViewModel();
        try
        {
            var definition = FirstReport(viewModel);
            var first = viewModel.OpenReport(definition);
            var second = viewModel.OpenReport(new ReportDefinition
            {
                Code = "leaders.sales",
                Title = "Лидеры продаж",
                Routine = new RoutineDefinition { Name = "reports.fn_sales_leaders" },
                Parameters = definition.Parameters,
                Result = new ResultDefinition()
            });
            Assert.Equal(2, viewModel.Tabs.Count);

            viewModel.CloseTab(second);
            Assert.Same(first, viewModel.SelectedTab);
            Assert.True(viewModel.IsReportSelected);

            viewModel.CloseTab(first);
            Assert.Empty(viewModel.Tabs);
            Assert.Null(viewModel.SelectedTab);
            Assert.False(viewModel.IsReportSelected);
        }
        finally { viewModel.Dispose(); }
    }

    [Fact]
    public void Vm08_CloseOtherTabsKeepsOnlyGivenTab()
    {
        var viewModel = CreateViewModel();
        try
        {
            var definition = FirstReport(viewModel);
            var first = viewModel.OpenReport(definition);
            viewModel.OpenReport(new ReportDefinition
            {
                Code = "leaders.sales",
                Title = "Лидеры продаж",
                Routine = new RoutineDefinition { Name = "reports.fn_sales_leaders" },
                Parameters = definition.Parameters,
                Result = new ResultDefinition()
            });
            viewModel.OpenReport(new ReportDefinition
            {
                Code = "leaders.city",
                Title = "Лидеры города-продаж",
                Routine = new RoutineDefinition { Name = "reports.fn_city_sales_leaders" },
                Parameters = definition.Parameters,
                Result = new ResultDefinition()
            });
            Assert.Equal(3, viewModel.Tabs.Count);

            viewModel.CloseOtherTabs(first);
            Assert.Same(first, Assert.Single(viewModel.Tabs));
            Assert.Same(first, viewModel.SelectedTab);
        }
        finally { viewModel.Dispose(); }
    }

    internal static ReportDefinition FirstReport(MainWindowViewModel viewModel) =>
        viewModel.Catalog.SelectMany(folder => folder.Children).First(node => node.Report is not null).Report!;

    internal static MainWindowViewModel CreateViewModel(int rowCount = 2) =>
        new(new StubCatalog(), new StubQuery(rowCount), new StubExport());

    private sealed class StubCatalog : IReportCatalogService
    {
        public ReportCatalog Load() => new("2.0",
            [new ReportDefinition
            {
                Code = "sales.monthly",
                Title = "Продажи за период",
                Routine = new RoutineDefinition { Name = "reports.fn_sales" },
                Parameters = [new ParameterDefinition { Name = "p_from", Type = "date" }, new ParameterDefinition { Name = "p_to", Type = "date" }],
                Result = new ResultDefinition { Columns = new Dictionary<string, ColumnDefinition> { ["amount"] = new() { Title = "Сумма" } } }
            }],
            [], ["sales"]);
    }

    private sealed class StubQuery(int rowCount) : IReportQueryService
    {
        public Task<ReportQueryResult> ExecuteAsync(ReportDefinition report, ReportFilter filter, CancellationToken cancellationToken = default)
        {
            var table = new DataTable();
            table.Columns.Add("amount", typeof(decimal));
            for (var i = 0; i < rowCount; i++) table.Rows.Add(i + 0.5m);
            return Task.FromResult(new ReportQueryResult(new ReportPreview(table, rowCount), new TemporaryCsvStage(), rowCount));
        }
    }

    private sealed class StubExport : IReportExportService
    {
        public ExportResult Save(ReportQueryResult result, ReportDefinition report, string targetPath) =>
            new(targetPath, "CSV", result.RowCount, false);
    }
}
