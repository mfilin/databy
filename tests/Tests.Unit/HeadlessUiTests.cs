using System.Data;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
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

            var grids = tableView.GetVisualDescendants().OfType<TableRowGrid>().ToArray();
            Assert.NotEmpty(grids);
            Assert.All(grids, grid => Assert.Equal(300, Assert.Single(grid.ColumnDefinitions).Width.Value));
        }
        finally { window.Close(); viewModel.Dispose(); }
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Ui_TableViewResizesOnlyRowGridsAndKeepsScrollbarAtRightEdge(int columnCount)
    {
        var (window, viewModel) = CreateWindow(columnCount: columnCount);
        window.Show();
        try
        {
            viewModel.OpenReport(MainWindowViewModelTests.FirstReport(viewModel));
            var table = new DataTable();
            for (var i = 0; i < columnCount; i++) table.Columns.Add($"Column{i}", typeof(string));
            table.Rows.Add(Enumerable.Range(0, columnCount).Select(i => (object)$"value{i}").ToArray());
            viewModel.SelectedTab!.Apply(new ReportQueryResult(new ReportPreview(table, 1), new TemporaryCsvStage(), 1));
            Relayout(window);

            var tableView = window.ResultsTable;
            var columns = viewModel.SelectedTab.TableColumns.ToArray();
            Assert.Equal(columnCount, columns.Length);

            columns[0].Width = 260;
            foreach (var other in columns.Skip(1)) other.Width = 300;
            Relayout(window);

            var templateGrids = tableView.GetVisualDescendants().OfType<Grid>()
                .Where(grid => grid is not TableRowGrid
                               && grid.ColumnDefinitions.Count == columnCount
                               && grid.ColumnDefinitions[0].Width.GridUnitType == GridUnitType.Star)
                .ToArray();

            var rowGrids = tableView.GetVisualDescendants().OfType<TableRowGrid>().ToArray();
            Assert.NotEmpty(rowGrids);
            Assert.All(rowGrids, grid => Assert.Equal(260, grid.ColumnDefinitions[0].Width.Value));
            Assert.All(templateGrids, grid =>
            {
                Assert.Equal(GridUnitType.Star, grid.ColumnDefinitions[0].Width.GridUnitType);
                Assert.Equal(1, grid.ColumnDefinitions[0].Width.Value);
            });

            var scrollViewer = tableView.GetVisualDescendants().OfType<ScrollViewer>().First();
            var scrollbar = scrollViewer.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>()
                .First(bar => bar.Orientation == Avalonia.Layout.Orientation.Horizontal);

            var needsScroll = scrollViewer.Extent.Width > scrollViewer.Viewport.Width;
            Assert.Equal(columnCount == 3, needsScroll);

            if (needsScroll)
            {
                // Полоса прокрутки прижата к правому краю: правый край ScrollBar совпадает с правым краем ScrollViewer.
                Assert.True(scrollbar.IsEffectivelyVisible, "Горизонтальная полоса прокрутки должна быть видна");
                var offset = Avalonia.VisualExtensions.TranslatePoint(
                    scrollbar, new Avalonia.Point(scrollbar.Bounds.Width, 0), scrollViewer);
                Assert.NotNull(offset);
                Assert.InRange(Math.Abs(offset!.Value.X - scrollViewer.Bounds.Width), 0, 1);
            }
        }
        finally { window.Close(); viewModel.Dispose(); }
    }

    [AvaloniaFact]
    public void Ui_TableViewUnsubscribesOldColumnsOnColumnsSourceChange()
    {
        var (window, viewModel) = CreateWindow(columnCount: 2);
        window.Show();
        try
        {
            viewModel.OpenReport(MainWindowViewModelTests.FirstReport(viewModel));
            var tableView = window.ResultsTable;

            var oldColumns = new System.Collections.ObjectModel.ObservableCollection<TableColumnViewModel>
            {
                new("A", "A", null),
                new("B", "B", null)
            };
            var newColumns = new System.Collections.ObjectModel.ObservableCollection<TableColumnViewModel>
            {
                new("C", "C", null),
                new("D", "D", null)
            };
            tableView.ColumnsSource = oldColumns;
            Relayout(window);
            tableView.ColumnsSource = newColumns;
            Relayout(window);

            var oldHandlers = CountPropertyChangedSubscribers(oldColumns[0]);
            var newHandlers = CountPropertyChangedSubscribers(newColumns[0]);
            Assert.Equal(0, oldHandlers);
            Assert.True(newHandlers > 0, "Новые колонки должны быть подписаны");

            oldColumns[0].Width = 500;
            Relayout(window);
            Assert.Equal(TableColumnViewModel.DefaultWidth * 0 + 500, oldColumns[0].Width);
        }
        finally { window.Close(); viewModel.Dispose(); }
    }

    private static int CountPropertyChangedSubscribers(TableColumnViewModel column)
    {
        var field = typeof(CommunityToolkit.Mvvm.ComponentModel.ObservableObject)
            .GetField("PropertyChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return field?.GetValue(column) is Delegate handler ? handler.GetInvocationList().Length : 0;
    }

    private static void Relayout(MainWindow window)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.Measure(new Avalonia.Size(1200, 800));
        window.Arrange(new Avalonia.Rect(0, 0, 1200, 800));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
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

    [AvaloniaFact]
    public async Task Ui_StatusBarTurnsGreenOnSuccess()
    {
        var (window, viewModel) = CreateWindow(amountFormat: "#,##0.00");
        window.Show();
        try
        {
            viewModel.OpenReport(MainWindowViewModelTests.FirstReport(viewModel));
            await viewModel.RunCommand.ExecuteAsync(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.True(viewModel.IsStatusSuccess);
            var bar = window.FindControl<Border>("statusBar");
            Assert.NotNull(bar);
            var brush = Assert.IsAssignableFrom<ISolidColorBrush>(bar!.Background);
            Assert.Equal(Color.Parse("#E8F5E9"), brush.Color);
        }
        finally { window.Close(); viewModel.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Ui_StatusBarTurnsYellowOnError()
    {
        var (window, viewModel) = CreateWindow(amountFormat: "#,##0.00", query: new ThrowingQuery());
        window.Show();
        try
        {
            viewModel.OpenReport(MainWindowViewModelTests.FirstReport(viewModel));
            await viewModel.RunCommand.ExecuteAsync(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.True(viewModel.IsStatusError);
            var bar = window.FindControl<Border>("statusBar");
            Assert.NotNull(bar);
            var brush = Assert.IsAssignableFrom<ISolidColorBrush>(bar!.Background);
            Assert.Equal(Color.Parse("#FFF8E1"), brush.Color);
        }
        finally { window.Close(); viewModel.Dispose(); }
    }

    internal static (MainWindow Window, MainWindowViewModel ViewModel) CreateWindow(string? amountFormat = null, int? columnCount = null, IReportQueryService? query = null)
    {
        var columns = new Dictionary<string, ReportColumnDefinition>(StringComparer.OrdinalIgnoreCase);
        if (amountFormat is not null)
            columns["Amount"] = new() { Title = "Сумма", Format = amountFormat };
        if (columnCount is { } count)
            for (var i = 0; i < count; i++)
                columns[$"Column{i}"] = new() { Title = $"Колонка {i + 1}" };
        var definition = new ReportDefinition
        {
            Code = "test.report",
            Title = "Тестовый отчёт",
            Routine = new RoutineDefinition { Name = "reports.fn_test" },
            Parameters = [new ParameterDefinition { Name = "p_from", Type = "date" }, new ParameterDefinition { Name = "p_to", Type = "date" }],
            Result = new ResultDefinition { Columns = columns }
        };
        var viewModel = new MainWindowViewModel(new StubCatalog(definition), query ?? new StubQuery(), new StubExport())
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
        public Task<ReportQueryResult> ExecuteAsync(ReportDefinition report, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
        {
            var table = new DataTable();
            table.Columns.Add("Amount", typeof(decimal));
            table.Rows.Add(1m);
            return Task.FromResult(new ReportQueryResult(new ReportPreview(table, 1), new TemporaryCsvStage(), 1));
        }
    }

    private sealed class ThrowingQuery : IReportQueryService
    {
        public Task<ReportQueryResult> ExecuteAsync(ReportDefinition report, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class StubExport : IReportExportService
    {
        public ExportResult Save(ReportQueryResult result, ReportDefinition report, string targetPath) =>
            new(targetPath, "CSV", result.RowCount, false);
    }
}
