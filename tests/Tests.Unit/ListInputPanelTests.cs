using System.Diagnostics;
using Databy.Reports.Catalog;
using Databy.Reports.Export;
using Databy.Reports.Input;
using Databy.Reports.Services;
using Databy.Reports.ViewModels;

namespace Tests.Unit;

[Trait("Category", "T1")]
[Collection("temp-stage")]
public sealed class ListInputPanelTests
{
    [Fact]
    public void Li01_PanelOnlyForReportsWithInput()
    {
        using var withInput = Create(listInput: true);
        using var without = Create(listInput: false);

        Assert.True(withInput.HasListInput);
        Assert.NotNull(withInput.SelectedListInput);
        Assert.False(without.HasListInput);
        Assert.Null(without.SelectedListInput);
    }

    [Fact]
    public async Task Li02_PasteFillsCountersPreviewAndErrors()
    {
        var clipboard = new StubClipboard("7707083893\t2026-01-01\n500100732259\tnot-a-date\n1111111111\t2026-02-02");
        using var viewModel = Create(listInput: true, clipboard: clipboard, typedDate: true);
        var panel = viewModel.SelectedListInput!;

        await panel.PasteCommand.ExecuteAsync(null);

        Assert.Equal(3, panel.TotalRows);
        Assert.Equal(2, panel.ValidRows);
        Assert.Equal(1, panel.ErrorRows);
        Assert.Equal(3, panel.PreviewRows.Count);
        Assert.Single(panel.Errors);
        Assert.Contains("строка", panel.Errors[0].Caption, StringComparison.OrdinalIgnoreCase);
        Assert.True(panel.IsReady);
    }

    [Fact]
    public async Task Li03_HeaderToggleAndMappingReparse()
    {
        var clipboard = new StubClipboard("ИНН\tДата\n7707083893\t2026-01-01");
        using var viewModel = Create(listInput: true, clipboard: clipboard);
        var panel = viewModel.SelectedListInput!;
        await panel.PasteCommand.ExecuteAsync(null);

        Assert.Equal(2, panel.TotalRows);
        Assert.Equal(["Колонка 1", "Колонка 2"], panel.PreviewHeaders);

        panel.FirstRowIsHeader = true;
        await panel.PendingReparse.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("ИНН", panel.PreviewHeaders[0]);
        Assert.Equal(1, panel.TotalRows);
        Assert.Equal(1, panel.PreviewRows.Count);

        var mapping = panel.Columns[0];
        var secondOption = mapping.SourceColumns.First(option => option.Index == 1);
        mapping.SelectedSource = secondOption;
        await panel.PendingReparse.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, panel.TotalRows);
    }

    [Fact]
    public async Task Li04_RunCallsListServiceWithParametersAndProgress()
    {
        var clipboard = new StubClipboard("7707083893");
        var listService = new StubListQuery();
        using var viewModel = Create(listInput: true, clipboard: clipboard, listService: listService);
        await viewModel.SelectedListInput!.PasteCommand.ExecuteAsync(null);

        await viewModel.RunCommand.ExecuteAsync(null);

        Assert.NotNull(listService.Report);
        Assert.Equal("lst.by_inn", listService.Report!.Code);
        Assert.Contains("p_asof", listService.Parameters!.Keys);
        Assert.NotNull(listService.Input);
        Assert.True(viewModel.HasData);
    }

    [Fact]
    public async Task Li05_CancelDisposesResult()
    {
        var clipboard = new StubClipboard("7707083893");
        var listService = new StubListQuery { Hang = true };
        using var viewModel = Create(listInput: true, clipboard: clipboard, listService: listService);
        await viewModel.SelectedListInput!.PasteCommand.ExecuteAsync(null);

        Assert.True(viewModel.RunCommand.CanExecute(null), "Команда запуска должна быть доступна");
        var run = viewModel.RunCommand.ExecuteAsync(null);
        await Task.Delay(200);
        viewModel.CancelCommand.Execute(null);
        await run;

        Assert.NotNull(listService.Input);
        Assert.StartsWith("Запуск отменён", viewModel.Status);
        Assert.False(viewModel.SelectedTab!.IsLoading);
    }

    [Fact]
    public async Task Li06_ClearDisposesStage()
    {
        var clipboard = new StubClipboard("7707083893");
        using var viewModel = Create(listInput: true, clipboard: clipboard);
        var panel = viewModel.SelectedListInput!;
        await panel.PasteCommand.ExecuteAsync(null);
        var path = panel.Stage!.Path;
        Assert.True(File.Exists(path));

        panel.ClearCommand.Execute(null);

        Assert.Null(panel.Stage);
        Assert.False(File.Exists(path));
        Assert.False(panel.IsReady);
        Assert.Equal(0, panel.TotalRows);
    }

    [Fact]
    public void Li07_RunDisabledWithoutList()
    {
        using var viewModel = Create(listInput: true);

        Assert.False(viewModel.CanRun);
        Assert.False(viewModel.RunCommand.CanExecute(null));
    }

    [Fact]
    public async Task Li08_HundredThousandRowsDoNotBlockUiThread()
    {
        var text = string.Join('\n', Enumerable.Range(1, 100_000).Select(i => "7707083893" + i));
        var clipboard = new StubClipboard(text);
        using var viewModel = Create(listInput: true, clipboard: clipboard);
        var panel = viewModel.SelectedListInput!;

        var parse = panel.PasteCommand.ExecuteAsync(null);
        var stopwatch = Stopwatch.StartNew();
        await parse;
        stopwatch.Stop();

        Assert.Equal(100_000, panel.TotalRows);
        Assert.True(panel.IsReady);
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 10_000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("Условие не выполнено");
            await Task.Delay(20);
        }
    }

    private static MainWindowViewModel Create(bool listInput, StubClipboard? clipboard = null, IListReportQueryService? listService = null, bool typedDate = false)
    {
        var definition = new ReportDefinition
        {
            Code = "lst.by_inn",
            Title = "По списку",
            Routine = new RoutineDefinition { Name = "lst.by_inn" },
            Parameters = [new ParameterDefinition { Name = "p_asof", Type = "date", Required = true }],
            Input = listInput
                ? new InputDefinition
                {
                    RowIdParameter = "p_row_id",
                    ResultKey = "row_id",
                    Columns = typedDate
                        ? [new InputColumnDefinition { Name = "p_inn", Type = "string" }, new InputColumnDefinition { Name = "p_dt", Type = "date" }]
                        : [new InputColumnDefinition { Name = "p_inn", Type = "string" }]
                }
                : null
        };
        var viewModel = new MainWindowViewModel(new StubCatalog(definition), new StubQuery(), new StubExport(),
            listQueryService: listService)
        {
            MinimumLoadingTime = TimeSpan.Zero,
            ClipboardServiceFactory = () => clipboard,
            FilePickerServiceFactory = () => null
        };
        viewModel.OpenReport(viewModel.Catalog[0].Children[0].Report!);
        return viewModel;
    }

    private sealed class StubClipboard(string text) : IClipboardService
    {
        public Task<string?> GetTextAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(text);
    }

    private sealed class StubListQuery : IListReportQueryService
    {
        public bool Hang { get; init; }
        public ReportDefinition? Report { get; private set; }
        public IReadOnlyDictionary<string, object?>? Parameters { get; private set; }
        public InputStage? Input { get; private set; }

        public async Task<ReportQueryResult> ExecuteAsync(ReportDefinition report,
            IReadOnlyDictionary<string, object?> parameters, InputStage input,
            IProgress<ListQueryProgress>? progress, CancellationToken cancellationToken)
        {
            Report = report;
            Parameters = parameters;
            Input = input;
            progress?.Report(new ListQueryProgress(1, 1, 1, 1));
            if (Hang) await Task.Delay(Timeout.Infinite, cancellationToken);
            var table = new System.Data.DataTable();
            table.Columns.Add("Колонка 1", typeof(string));
            table.Columns.Add("name", typeof(string));
            table.Columns.Add("Статус поиска", typeof(string));
            table.Rows.Add("7707083893", "Ромашка", "Найдено");
            return new ReportQueryResult(new ReportPreview(table, 1), new TemporaryCsvStage(), 1);
        }
    }

    private sealed class StubCatalog(ReportDefinition definition) : IReportCatalogService
    {
        public ReportCatalog Load() => new("1", [definition], [], ["lst"]);
    }

    private sealed class StubQuery : IReportQueryService
    {
        public Task<ReportQueryResult> ExecuteAsync(ReportDefinition report, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
        {
            var table = new System.Data.DataTable();
            table.Columns.Add("value", typeof(int));
            table.Rows.Add(1);
            return Task.FromResult(new ReportQueryResult(new ReportPreview(table, 1), new TemporaryCsvStage(), 1));
        }
    }

    private sealed class StubExport : IReportExportService
    {
        public ExportResult Save(ReportQueryResult result, ReportDefinition report, string targetPath) =>
            new(targetPath, "CSV", result.RowCount, false);
    }
}
