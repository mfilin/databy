using System.Data;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Logging;
using Databy.Reports.Catalog;
using Databy.Reports.Export;
using Databy.Reports.Input;
using Databy.Reports.Services;
using Databy.Reports.ViewModels;
using Databy.Reports.Views;

namespace Tests.Unit;

[Trait("Category", "T1")]
public sealed class ListInputUiTests
{
    /// <summary>
    /// Предпросмотр списка — оверлей над таблицей результатов. После запуска отчёта
    /// он должен скрываться, иначе закрывает собой готовые данные.
    /// </summary>
    [AvaloniaFact]
    public async Task Li09_PreviewOverlayHidesAfterReportRun()
    {
        var sink = new CapturingSink();
        var oldSink = Logger.Sink;
        Logger.Sink = sink;
        try
        {
            var definition = new ReportDefinition
            {
                Code = "lst.by_inn",
                Title = "По списку",
                Routine = new RoutineDefinition { Name = "lst.by_inn" },
                Parameters = [new ParameterDefinition { Name = "p_asof", Type = "date", Required = true }],
                Input = new InputDefinition
                {
                    RowIdParameter = "p_row_id",
                    ResultKey = "row_id",
                    Columns = [new InputColumnDefinition { Name = "p_inn", Type = "string" }]
                }
            };
            var viewModel = new MainWindowViewModel(new StubCatalog(definition), new StubQuery(), new StubExport(),
                listQueryService: new StubListQuery())
            {
                MinimumLoadingTime = TimeSpan.Zero
            };
            var window = new MainWindow { DataContext = viewModel };
            viewModel.ClipboardServiceFactory = () => new StubClipboard("7707083893");
            viewModel.FilePickerServiceFactory = () => null;
            window.Show();
            try
            {
                viewModel.OpenReport(viewModel.Catalog[0].Children[0].Report!);
                await viewModel.SelectedListInput!.PasteCommand.ExecuteAsync(null);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                Assert.True(viewModel.HasListPreview);
                Assert.True(window.FindControl<Border>("listPreview")!.IsVisible);
                Assert.True(window.FindControl<StackPanel>("listInputPanel")!.IsVisible);

                await viewModel.RunCommand.ExecuteAsync(null);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                Assert.True(viewModel.HasData);
                Assert.False(viewModel.HasListPreview);
                Assert.False(window.FindControl<Border>("listPreview")!.IsVisible);
            }
            finally { window.Close(); viewModel.Dispose(); }
        }
        finally { Logger.Sink = oldSink; }

        Assert.DoesNotContain(sink.Messages,
            message => message.Contains("HasListPreview", StringComparison.Ordinal)
                       || message.Contains("HasListInput", StringComparison.Ordinal));
    }

    private sealed class CapturingSink : ILogSink
    {
        public List<string> Messages { get; } = [];
        public bool IsEnabled(LogEventLevel level, string area) => true;

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
            Messages.Add($"{level} {area}: {messageTemplate}");

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues) =>
            Messages.Add($"{level} {area}: {messageTemplate} [{string.Join(", ", propertyValues)}]");
    }

    private sealed class StubClipboard(string text) : IClipboardService
    {
        public Task<string?> GetTextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(text);
    }

    private sealed class StubListQuery : IListReportQueryService
    {
        public Task<ReportQueryResult> ExecuteAsync(ReportDefinition report, IReadOnlyDictionary<string, object?> parameters,
            InputStage input, IProgress<ListQueryProgress>? progress, CancellationToken cancellationToken)
        {
            var table = new DataTable();
            table.Columns.Add("Колонка 1", typeof(string));
            table.Columns.Add("name", typeof(string));
            table.Columns.Add("Статус поиска", typeof(string));
            table.Rows.Add("7707083893", "Ромашка", "Найдено");
            return Task.FromResult(new ReportQueryResult(new ReportPreview(table, 1), new TemporaryCsvStage(), 1));
        }
    }

    private sealed class StubCatalog(ReportDefinition definition) : IReportCatalogService
    {
        public ReportCatalog Load() => new("1", [definition], [], ["lst"]);
    }

    private sealed class StubQuery : IReportQueryService
    {
        public Task<ReportQueryResult> ExecuteAsync(ReportDefinition report, IReadOnlyDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ReportQueryResult(new ReportPreview(new DataTable(), 0), new TemporaryCsvStage(), 0));
    }

    private sealed class StubExport : IReportExportService
    {
        public ExportResult Save(ReportQueryResult result, ReportDefinition report, string targetPath) =>
            new(targetPath, "CSV", result.RowCount, false);
    }
}