using System.Data;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
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
    public void Ui04_HeadlessWindowLoadsCatalogWithoutSelectedReport()
    {
        var (window, viewModel) = CreateWindow();
        window.Show();
        try
        {
            Assert.IsType<Grid>(window.Content);
            Assert.NotEmpty(viewModel.Catalog);
            Assert.Empty(viewModel.Tabs);
            Assert.Null(viewModel.SelectedTab);
            Assert.False(viewModel.IsReportSelected);
            Assert.False(viewModel.HasData);
            Assert.False(viewModel.ShowEmptyState);
        }
        finally { window.Close(); viewModel.Dispose(); }
    }

    [AvaloniaFact]
    public void Ui_TableViewRendersHeaderAndFormattedRows()
    {
        var (window, viewModel) = CreateWindow(amountFormat: "#,##0.00");
        window.Show();
        try
        {
            viewModel.OpenReport(MainWindowViewModelTests.FirstReport(viewModel));
            var table = new DataTable();
            table.Columns.Add("Amount", typeof(decimal));
            table.Rows.Add(1234.5m);
            var result = new ReportQueryResult(new ReportPreview(table, 1), new TemporaryCsvStage(), 1);
            viewModel.SelectedTab!.Apply(result);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var tab = viewModel.SelectedTab!;
            var column = Assert.Single(tab.TableColumns);
            Assert.Equal("Сумма", column.Title);
            Assert.Equal(2, tab.TableRows.Count);
            Assert.True(tab.TableRows[0].IsHeader);
            Assert.Equal("Сумма", tab.TableRows[0][0]);
            Assert.Equal(1234.5m.ToString("#,##0.00", System.Globalization.CultureInfo.CurrentCulture), tab.TableRows[1][0]);
            Assert.IsType<TableView>(window.ResultsTable);
        }
        finally { window.Close(); viewModel.Dispose(); }
    }

    [Fact]
    public void Column_ResizeClampsToMinimumAndFormatsWidth()
    {
        var column = new TableColumnViewModel("amount", "Сумма", "#,##0.00");

        Assert.Equal(TableColumnViewModel.DefaultWidth, column.Width);
        Assert.Equal("160px", column.WidthText);

        column.Resize(220.4);
        Assert.Equal(220, column.Width);
        Assert.Equal("220px", column.WidthText);

        column.Resize(10);
        Assert.Equal(TableColumnViewModel.MinimumWidth, column.Width);
    }

    [Fact]
    public void Column_UsesConfiguredPixelWidthOverDefault()
    {
        var column = new TableColumnViewModel("amount", "Сумма", null, 240);

        Assert.Equal(240, column.Width);
        Assert.Equal("240px", column.WidthText);
    }

    [AvaloniaFact]
    public void Ui_TableViewResizesRowGridsWhenColumnWidthChanges()
    {
        var (window, viewModel) = CreateWindow(amountFormat: "#,##0.00");
        window.Show();
        try
        {
            viewModel.OpenReport(MainWindowViewModelTests.FirstReport(viewModel));
            var table = new DataTable();
            table.Columns.Add("Amount", typeof(decimal));
            table.Rows.Add(1m);
            viewModel.SelectedTab!.Apply(new ReportQueryResult(new ReportPreview(table, 1), new TemporaryCsvStage(), 1));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.Measure(new Avalonia.Size(1200, 800));
            window.Arrange(new Avalonia.Rect(0, 0, 1200, 800));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var tableView = window.ResultsTable;
            var firstColumn = Assert.Single(viewModel.SelectedTab.TableColumns);
            firstColumn.Width = 300;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.Measure(new Avalonia.Size(1200, 800));
            window.Arrange(new Avalonia.Rect(0, 0, 1200, 800));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var grids = tableView.GetVisualDescendants().OfType<Grid>()
                .Where(candidate => candidate.ColumnDefinitions.Count == 1).ToArray();
            Assert.NotEmpty(grids);
            Assert.All(grids, grid => Assert.Equal(300, grid.ColumnDefinitions[0].Width.Value));
        }
        finally { window.Close(); viewModel.Dispose(); }
    }

    [AvaloniaFact]
    public void Ui_TabCloseButtonClosesTab()
    {
        var (window, viewModel) = CreateWindow();
        window.Show();
        try
        {
            viewModel.OpenReport(MainWindowViewModelTests.FirstReport(viewModel));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Single(viewModel.Tabs);

            var closeButton = window.GetVisualDescendants().OfType<Button>()
                .First(button => button.Classes.Contains("tabClose"));
            closeButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Empty(viewModel.Tabs);
            Assert.Null(viewModel.SelectedTab);
        }
        finally { window.Close(); viewModel.Dispose(); }
    }

    [AvaloniaFact]
    public void Ui_TabContentDoesNotShowViewModelTypeName()
    {
        var (window, viewModel) = CreateWindow();
        window.Show();
        try
        {
            viewModel.OpenReport(MainWindowViewModelTests.FirstReport(viewModel));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var texts = window.GetVisualDescendants().OfType<TextBlock>()
                .Select(block => block.Text ?? string.Empty).ToArray();
            Assert.DoesNotContain(texts, text => text.Contains(nameof(ReportTabViewModel), StringComparison.Ordinal));
        }
        finally { window.Close(); viewModel.Dispose(); }
    }

    [AvaloniaFact]
    public void Ui_TableViewCreatesVisualRowsWhenDataAppears()
    {
        var (window, viewModel) = CreateWindow(amountFormat: "#,##0.00");
        window.Show();
        try
        {
            viewModel.OpenReport(MainWindowViewModelTests.FirstReport(viewModel));
            var table = new DataTable();
            table.Columns.Add("Amount", typeof(decimal));
            table.Rows.Add(10m);
            table.Rows.Add(20m);
            viewModel.SelectedTab!.Apply(new ReportQueryResult(new ReportPreview(table, 2), new TemporaryCsvStage(), 2));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.Measure(new Avalonia.Size(1200, 800));
            window.Arrange(new Avalonia.Rect(0, 0, 1200, 800));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var table2 = window.ResultsTable;
            Assert.True(viewModel.HasData);
            Assert.True(table2.IsEffectivelyVisible);
            var texts = table2.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
            Assert.True(texts.Length > 0, $"items={table2.ItemCount}; visible={table2.IsEffectivelyVisible}; texts={texts.Length}");
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
        var viewModel = new MainWindowViewModel(new StubCatalog(definition), new StubQuery(), new StubExport())
        {
            MinimumLoadingTime = TimeSpan.Zero
        };
        return (new MainWindow { DataContext = viewModel }, viewModel);
    }

    private sealed class StubCatalog(ReportDefinition report) : IReportCatalogService
    {
        public ReportCatalog Load() => new("1.0", [report], [], ["test"]);
    }

    private sealed class StubQuery : IReportQueryService
    {
        public Task<ReportQueryResult> ExecuteAsync(ReportDefinition report, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ReportQueryResult(new ReportPreview(new DataTable(), 0), new TemporaryCsvStage(), 0));
    }

    private sealed class StubExport : IReportExportService
    {
        public ExportResult Save(ReportQueryResult result, ReportDefinition report, string targetPath) =>
            new(targetPath, "CSV", result.RowCount, false);
    }
}
