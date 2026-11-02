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
    public void Vm01_BuildsCatalogTreeAndSelectsFirstReport()
    {
        var viewModel = CreateViewModel();
        try
        {
            var root = Assert.Single(viewModel.Catalog);
            Assert.True(root.IsFolder);
            var folder = Assert.Single(root.Children);
            Assert.Equal("sales", folder.Title);
            Assert.True(folder.IsFolder);
            Assert.False(folder.Children[0].IsFolder);
            Assert.Equal("sales.monthly", viewModel.SelectedTab?.Code);
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
            var definition = viewModel.SelectedTab!.Definition;
            var first = viewModel.OpenReport(definition);
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
            viewModel.SetPeriod(new DateTimeOffset(new DateTime(2026, 5, 10)), new DateTimeOffset(new DateTime(2026, 5, 1)));
            await viewModel.RunCommand.ExecuteAsync(null);
            Assert.Contains("не может быть раньше", viewModel.Status);
            Assert.False(viewModel.HasData);
        }
        finally { viewModel.Dispose(); }
    }

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