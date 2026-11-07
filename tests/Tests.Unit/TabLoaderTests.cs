using System.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Databy.Reports.Catalog;
using Databy.Reports.Export;
using Databy.Reports.Services;
using Databy.Reports.ViewModels;
using Databy.Reports.Views;

namespace Tests.Unit;

[Trait("Category", "T1")]
public sealed class TabLoaderTests
{
    [AvaloniaFact]
    public async Task Tl02_EscapeCancelsRunningReport()
    {
        var query = new SlowlyRespondingQuery();
        var viewModel = new MainWindowViewModel(new StubCatalog(), query, new StubExport())
        {
            MinimumLoadingTime = TimeSpan.Zero
        };
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        try
        {
            var tab = viewModel.OpenReport(viewModel.Catalog[0].Children[0].Report!);
            var run = viewModel.RunCommand.ExecuteAsync(null);
            await Task.Delay(100);
            Assert.True(tab.IsLoading);

            window.KeyPress(Key.Escape, RawInputModifiers.None);
            for (var i = 0; i < 3; i++) Dispatcher.UIThread.RunJobs();

            var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(5))) == run;
            Assert.True(finished, "Esc должен прерывать выполнение запроса");
            Assert.False(tab.IsLoading);
            Assert.StartsWith("Запуск отменён", viewModel.Status);
        }
        finally { window.Close(); viewModel.Dispose(); }
    }

    [Fact]
    public async Task Tl01_LoadingStateIsPerTabAndCancelTargetsItsownRun()
    {
        var gate = new TaskCompletionSource();
        var query = new GatedQuery(gate.Task);
        var viewModel = new MainWindowViewModel(new StubCatalog(), query, new StubExport())
        {
            MinimumLoadingTime = TimeSpan.Zero
        };
        try
        {
            var first = viewModel.OpenReport(viewModel.Catalog[0].Children[0].Report!);
            var second = viewModel.OpenReport(viewModel.Catalog[1].Children[0].Report!);

            viewModel.SelectedTab = first;
            var firstRun = viewModel.RunCommand.ExecuteAsync(null);
            await Task.Delay(50);

            Assert.True(first.IsLoading, "Первая вкладка должна показывать загрузку");
            Assert.False(second.IsLoading, "Вторая вкладка не должна показывать загрузку");
            Assert.True(viewModel.IsBusy);

            viewModel.SelectedTab = second;
            Assert.False(viewModel.SelectedTab.IsLoading);

            viewModel.SelectedTab = first;
            viewModel.CancelCommand.Execute(null);
            gate.TrySetResult();
            await firstRun.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.False(first.IsLoading);
            Assert.StartsWith("Запуск отменён", viewModel.Status);
            Assert.Null(first.CurrentResult);
        }
        finally { viewModel.Dispose(); }
    }

    private sealed class SlowlyRespondingQuery : IReportQueryService
    {
        public async Task<ReportQueryResult> ExecuteAsync(ReportDefinition report, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            var table = new DataTable();
            table.Columns.Add("amount", typeof(decimal));
            table.Rows.Add(1m);
            return new ReportQueryResult(new ReportPreview(table, 1), new TemporaryCsvStage(), 1);
        }
    }

    private sealed class GatedQuery(Task gate) : IReportQueryService
    {
        public async Task<ReportQueryResult> ExecuteAsync(ReportDefinition report, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken);
            var table = new DataTable();
            table.Columns.Add("amount", typeof(decimal));
            table.Rows.Add(1m);
            return new ReportQueryResult(new ReportPreview(table, 1), new TemporaryCsvStage(), 1);
        }
    }

    private sealed class StubCatalog : IReportCatalogService
    {
        public ReportCatalog Load() => new("1.0",
            [
                Definition("sales.monthly", "Продажи за период"),
                Definition("leaders.sales", "Лидеры продаж")
            ], [], ["sales"]);

        private static ReportDefinition Definition(string code, string title) => new()
        {
            Code = code,
            Title = title,
            Routine = new RoutineDefinition { Name = "reports.fn_test" },
            Parameters = [],
            Result = new ResultDefinition { Columns = new Dictionary<string, ColumnDefinition> { ["amount"] = new() { Title = "Сумма" } } }
        };
    }

    private sealed class StubExport : IReportExportService
    {
        public ExportResult Save(ReportQueryResult result, ReportDefinition report, string targetPath) =>
            new(targetPath, "CSV", result.RowCount, false);
    }
}
