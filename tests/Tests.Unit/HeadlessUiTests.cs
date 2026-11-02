using System.Data;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Databy.Reports.Catalog;
using Databy.Reports.Export;
using Databy.Reports.Services;
using Databy.Reports.ViewModels;
using Databy.Reports.Views;
using ReportColumnDefinition = Databy.Reports.Catalog.ColumnDefinition;

namespace Tests.Unit;

[Trait("Category", "T1")]
public sealed class HeadlessUiTests
{
    [AvaloniaFact]
    public void Ui04_HeadlessWindowLoadsCatalogAndShowsEmptyState()
    {
        var (window, viewModel) = CreateWindow();
        window.Show();
        try
        {
            Assert.IsType<Grid>(window.Content);
            Assert.NotEmpty(viewModel.Catalog);
            Assert.Single(viewModel.Tabs);
            Assert.NotNull(viewModel.SelectedTab);
            Assert.False(viewModel.HasData);
            Assert.True(viewModel.ShowEmptyState);
        }
        finally { window.Close(); viewModel.Dispose(); }
    }

    [AvaloniaFact]
    public void Ui_NumericColumnFormatKeepsValuesVisible()
    {
        var (window, viewModel) = CreateWindow(amountFormat: "#,##0.00");
        window.Show();
        try
        {
            var table = new DataTable();
            table.Columns.Add("Amount", typeof(decimal));
            table.Rows.Add(1234.5m);
            var result = new ReportQueryResult(new ReportPreview(table, 1), new TemporaryCsvStage(), 1);
            viewModel.SelectedTab!.Apply(result);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var column = Assert.IsType<DataGridTextColumn>(Assert.Single(window.ResultsGrid.Columns));
            Assert.Equal("Сумма", column.Header);
            Assert.Equal("{0:#,##0.00}", Assert.IsType<Avalonia.Data.Binding>(column.Binding).StringFormat);
        }
        finally { window.Close(); viewModel.Dispose(); }
    }

    internal static (MainWindow Window, MainWindowViewModel ViewModel) CreateWindow(string? amountFormat = null)
    {
        var columns = new Dictionary<string, ReportColumnDefinition>(StringComparer.OrdinalIgnoreCase);
        if (amountFormat is not null)
            columns["Amount"] = new() { Title = "Сумма", Format = amountFormat };
        var definition = new ReportDefinition
        {
            Code = "test.report",
            Title = "Тестовый отчёт",
            Routine = new RoutineDefinition { Name = "reports.fn_test" },
            Parameters = [new ParameterDefinition { Name = "p_from", Type = "date" }, new ParameterDefinition { Name = "p_to", Type = "date" }],
            Result = new ResultDefinition { Columns = columns }
        };
        var viewModel = new MainWindowViewModel(new StubCatalog(definition), new StubQuery(), new StubExport());
        viewModel.OpenReport(definition);
        return (new MainWindow { DataContext = viewModel }, viewModel);
    }

    private sealed class StubCatalog(ReportDefinition report) : IReportCatalogService
    {
        public ReportCatalog Load() => new("1.0", [report], [], []);
    }

    private sealed class StubQuery : IReportQueryService
    {
        public Task<ReportQueryResult> ExecuteAsync(ReportDefinition report, ReportFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ReportQueryResult(new ReportPreview(new DataTable(), 0), new TemporaryCsvStage(), 0));
    }

    private sealed class StubExport : IReportExportService
    {
        public ExportResult Save(ReportQueryResult result, ReportDefinition report, string targetPath) =>
            new(targetPath, "CSV", result.RowCount, false);
    }
}