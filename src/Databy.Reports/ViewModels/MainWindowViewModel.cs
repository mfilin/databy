using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Databy.Reports.Catalog;
using Databy.Reports.Services;

namespace Databy.Reports.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    public const int PageSize = 100;
    public TimeSpan MinimumLoadingTime { get; set; } = TimeSpan.FromMilliseconds(500);
    private readonly IReportCatalogService _catalogService;
    private readonly IReportQueryService _queryService;
    private readonly IListReportQueryService? _listQueryService;
    private readonly IReportExportService _exportService;
    private readonly IReportAuditService _auditService;

    public MainWindowViewModel(
        IReportCatalogService catalogService,
        IReportQueryService queryService,
        IReportExportService exportService,
        IReportAuditService? auditService = null,
        IListReportQueryService? listQueryService = null)
    {
        _catalogService = catalogService;
        _queryService = queryService;
        _exportService = exportService;
        _auditService = auditService ?? NullReportAuditService.Instance;
        _listQueryService = listQueryService;
        Tabs.CollectionChanged += OnTabsChanged;
        LoadCatalog();
        ApplyDefaultPeriod();
    }

    public ObservableCollection<CatalogNode> Catalog { get; } = [];
    public ObservableCollection<ReportTabViewModel> Tabs { get; } = [];
    public ObservableCollection<ReportColumnViewModel> Columns { get; } = [];

    [ObservableProperty]
    private ReportTabViewModel? _selectedTab;

    [ObservableProperty]
    private DateTimeOffset? _fromDate;

    [ObservableProperty]
    private DateTimeOffset? _toDate;

    [ObservableProperty]
    private DateTimeOffset? _filterFromDate;

    [ObservableProperty]
    private DateTimeOffset? _filterToDate;

    [ObservableProperty]
    private string? _status = "Выберите отчёт в каталоге и нажмите «Обновить»";

    [ObservableProperty]
    private string _catalogVersion = "не указана";

    [ObservableProperty]
    private string? _catalogError;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _page;

    public bool IsReportSelected => SelectedTab is not null;
    public string SelectedTabTitle => SelectedTab?.Title ?? "Отчёт не выбран";
    public bool HasReportParameters => SelectedTab?.Definition.Parameters.Count > 0;
    public bool HasDateParameters => SelectedTab?.Definition.Parameters
        .Any(parameter => string.Equals(parameter.Type, "date", StringComparison.OrdinalIgnoreCase)) == true;
    public bool HasNoParameters => SelectedTab is not null && !HasReportParameters;
    public IEnumerable<TableColumnViewModel> SelectedTabColumns => SelectedTab?.TableColumns ?? [];
    public IEnumerable<TableRowViewModel> SelectedTabRows => SelectedTab?.TableRows ?? [];
    public bool CanRun => SelectedTab is not null && !IsBusy && SelectedTab.ListInput is null or { IsReady: true };
    public bool CanCancelRun => IsBusy;
    public bool CanExport => SelectedTab?.CurrentResult is not null && !IsBusy;
    public bool CanGoBack => Page > 0 && !IsBusy;
    public bool CanGoForward => Page < PageCount - 1 && !IsBusy;
    public bool HasData => SelectedTab?.HasRows == true;
    public bool HasColumns => Columns.Count > 0;
    public bool ShowEmptyState => SelectedTab is not null && !IsBusy && !HasData;
    public string EmptyMessage => HasData ? string.Empty : "Нет данных";
    public int PageCount => SelectedTab?.PageCount ?? 1;
    public string PageCaption => $"{Page + 1} из {PageCount}";
    public string TotalCaption => SelectedTab is null ? string.Empty : $"{SelectedTab.TotalRows:N0} строк";

    public Func<ReportTabViewModel, Task<string?>> ExportPathProvider { get; set; } = _ => Task.FromResult<string?>(null);

    /// <summary>Фабрики сервисов буфера и выбора файла (задаются представлением).</summary>
    public Func<IClipboardService?>? ClipboardServiceFactory { get; set; }

    public Func<IFilePickerService?>? FilePickerServiceFactory { get; set; }

    public ListInputViewModel? SelectedListInput => SelectedTab?.ListInput;
    public bool HasListInput => SelectedTab?.ListInput is not null;
    public bool HasListPreview => SelectedTab?.ListInput is { HasPreview: true } && !HasData;

    public void LoadCatalog()
    {
        Catalog.Clear();
        try
        {
            var catalog = _catalogService.Load();
            CatalogVersion = catalog.Version;
            CatalogError = catalog.Errors.Count == 0 ? null : $"Файлов с ошибками: {catalog.Errors.Count}";
            BuildTree(catalog);
            Status = CatalogError is null
                ? $"Каталог v{CatalogVersion} • {catalog.Reports.Count} отчёт(а)"
                : $"Каталог v{CatalogVersion} • {catalog.Reports.Count} отчёт(а) • ошибок: {catalog.Errors.Count}";
        }
        catch (Exception ex)
        {
            CatalogError = ex.Message;
            Status = "Не удалось загрузить каталог: " + ex.Message;
        }
    }

    public ReportTabViewModel OpenReport(ReportDefinition definition)
    {
        var existing = Tabs.FirstOrDefault(tab => tab.Code == definition.Code);
        if (existing is not null)
        {
            SelectedTab = existing;
            return existing;
        }
        var listInput = definition.Input is null
            ? null
            : new ListInputViewModel(definition, ClipboardServiceFactory?.Invoke(), FilePickerServiceFactory?.Invoke());
        var created = new ReportTabViewModel(definition, listInput);
        if (listInput is not null) listInput.ReadyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasListPreview));
            NotifyCommandsChanged();
        };
        Tabs.Add(created);
        SelectedTab = created;
        ApplyDefaultPeriod();
        OnPropertyChanged(nameof(IsReportSelected));
        return created;
    }

    public void CloseTab(ReportTabViewModel? tab)
    {
        if (tab is null || !Tabs.Contains(tab)) return;
        var index = Tabs.IndexOf(tab);
        var wasSelected = ReferenceEquals(SelectedTab, tab);
        Tabs.Remove(tab);
        tab.Dispose();
        if (wasSelected)
            SelectedTab = Tabs.Count == 0 ? null : Tabs[Math.Min(index, Tabs.Count - 1)];
        OnPropertyChanged(nameof(IsReportSelected));
        NotifyLayoutChanged();
    }

    public void CloseOtherTabs(ReportTabViewModel? keep)
    {
        foreach (var tab in Tabs.Where(tab => !ReferenceEquals(tab, keep)).ToList()) CloseTab(tab);
        if (keep is not null && Tabs.Contains(keep)) SelectedTab = keep;
    }

    public void CloseAllTabs()
    {
        foreach (var tab in Tabs.ToList()) CloseTab(tab);
        SelectedTab = null;
    }

    public void SetPeriod(DateTimeOffset from, DateTimeOffset to)
    {
        FromDate = from;
        ToDate = to;
        FilterFromDate = from;
        FilterToDate = to;
    }

    public void GoToPage(int page)
    {
        if (SelectedTab is null) return;
        SelectedTab.GoToPage(page);
        SelectedTab.Rows = SelectedTab.GetPageView(PageSize);
        SelectedTab.BuildTable();
        Page = SelectedTab.Page;
        NotifyLayoutChanged();
    }

    partial void OnSelectedTabChanged(ReportTabViewModel? value)
    {
        Page = value?.Page ?? 0;
        Columns.Clear();
        if (value is not null)
            foreach (var column in value.Definition.Result?.Columns ?? new Dictionary<string, ColumnDefinition>())
                Columns.Add(new ReportColumnViewModel(column.Key, column.Value));
        OnPropertyChanged(nameof(SelectedTabTitle));
        OnPropertyChanged(nameof(HasReportParameters));
        OnPropertyChanged(nameof(HasDateParameters));
        OnPropertyChanged(nameof(HasNoParameters));
        OnPropertyChanged(nameof(SelectedTabColumns));
        OnPropertyChanged(nameof(SelectedTabRows));
        OnPropertyChanged(nameof(HasColumns));
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(IsReportSelected));
        OnPropertyChanged(nameof(HasListInput));
        OnPropertyChanged(nameof(SelectedListInput));
        OnPropertyChanged(nameof(HasListPreview));
        NotifyCommandsChanged();
        NotifyLayoutChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEmptyState));
        NotifyCommandsChanged();
        NotifyLayoutChanged();
    }

    private static long ElapsedMs(DateTimeOffset startedAt) =>
        (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;

    private async Task EnsureMinimumLoadingTimeAsync(DateTimeOffset startedAt, CancellationToken cancellationToken)
    {
        var remaining = MinimumLoadingTime - (DateTimeOffset.UtcNow - startedAt);
        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining, cancellationToken);
    }

    private IReadOnlyDictionary<string, object?> BuildParameters(ReportDefinition report)
    {
        var from = DateOnly.FromDateTime((FilterFromDate ?? FromDate ?? DefaultFrom()).Date);
        var to = DateOnly.FromDateTime((FilterToDate ?? ToDate ?? DateTimeOffset.Now).Date);
        if (to < from)
            throw new ArgumentException("Дата «по» не может быть раньше даты «с»");

        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var today = DateOnly.FromDateTime(DateTime.Today);
        foreach (var parameter in report.Parameters)
        {
            var resolved = ParameterDefaults.Resolve(parameter, today);
            if (resolved is not null) values[parameter.Name] = resolved;
        }

        var firstDate = true;
        foreach (var parameter in report.Parameters.Where(parameter => string.Equals(parameter.Type, "date", StringComparison.OrdinalIgnoreCase)))
        {
            values[parameter.Name] = firstDate ? from : to;
            firstDate = false;
        }

        foreach (var parameter in report.Parameters)
        {
            if (!parameter.Required) continue;
            if (values.TryGetValue(parameter.Name, out var value) && value is not null) continue;
            throw new ArgumentException($"Не задано значение обязательного параметра '{parameter.Name}'", parameter.Name);
        }
        return values;
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RunAsync() => SelectedTab is { } tab ? RunReportAsync(tab) : Task.CompletedTask;

    private async Task RunReportAsync(ReportTabViewModel tab)
    {
        if (tab.IsLoading) return;
        var cts = new CancellationTokenSource();
        tab.RunCancellation?.Dispose();
        tab.RunCancellation = cts;
        tab.IsLoading = true;
        IsBusy = true;
        Status = $"Выполняется запрос… ({tab.Title})";
        var startedAt = DateTimeOffset.UtcNow;
        ReportQueryResult? result = null;
        var applied = false;
        try
        {
            var parameters = BuildParameters(tab.Definition);
            _auditService.ReportStarted(tab.Definition, parameters);
            if (tab.ListInput is { Stage: { } listStage } listInput && _listQueryService is not null)
            {
                var progress = new Progress<ListQueryProgress>(report =>
                {
                    tab.IsLoading = report.TotalRows > 0 && !report.IsFinal;
                    Status = $"Обработано {report.ProcessedRows:N0} из {report.TotalRows:N0} строк ({tab.Title})";
                });
                result = await _listQueryService.ExecuteAsync(tab.Definition, parameters, listStage, progress, cts.Token);
            }
            else
            {
                result = await _queryService.ExecuteAsync(tab.Definition, parameters, cts.Token);
            }
            await EnsureMinimumLoadingTimeAsync(startedAt, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            EnsureVisibleColumns(tab, result);
            tab.Apply(result);
            applied = true;
            if (ReferenceEquals(SelectedTab, tab)) Page = 0;
            Status = $"Показано {tab.PreviewRows} из {tab.TotalRows:N0} строк" +
                     (tab.TotalRows > tab.PreviewRows ? " (превью ограничено)" : string.Empty);
            _auditService.ReportSucceeded(tab.Definition, tab.TotalRows, ElapsedMs(startedAt));
        }
        catch (OperationCanceledException)
        {
            Status = $"Запуск отменён: {tab.Title}";
            _auditService.ReportCanceled(tab.Definition, ElapsedMs(startedAt));
        }
        catch (Exception ex)
        {
            Status = "Ошибка отчёта: " + ex.Message;
            _auditService.ReportFailed(tab.Definition, ex, ElapsedMs(startedAt));
            Serilog.Log.Error(ex, "Report {ReportCode} failed", tab.Code);
        }
        finally
        {
            if (!applied) result?.Dispose();
            tab.IsLoading = false;
            if (ReferenceEquals(tab.RunCancellation, cts))
            {
                tab.RunCancellation = null;
                cts.Dispose();
            }
            IsBusy = Tabs.Any(other => other.IsLoading);
            OnPropertyChanged(nameof(CanExport));
            NotifyLayoutChanged();
        }
    }

    private static void EnsureVisibleColumns(ReportTabViewModel tab, ReportQueryResult result)
    {
        var databaseColumns = result.Preview.Table.Columns.Cast<System.Data.DataColumn>().Select(column => column.ColumnName);
        var visible = ResultColumns.Resolve(databaseColumns, tab.Definition.Result);
        if (visible.Count == 0)
            throw new InvalidOperationException("Все колонки скрыты в YAML отчёта");
    }

    [RelayCommand(CanExecute = nameof(CanCancelRun))]
    private void Cancel() => SelectedTab?.CancelRun();

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (SelectedTab is not { CurrentResult: { } result } tab) return;
        var path = await ExportPathProvider(tab);
        if (path is null) return;
        try
        {
            var saved = _exportService.Save(result, tab.Definition, path);
            Status = saved.ExcelLimitExceeded
                ? $"Превышен лимит Excel — сохранён полный CSV: {Path.GetFileName(saved.Path)}"
                : "Файл сохранён: " + Path.GetFileName(saved.Path);
        }
        catch (Exception ex)
        {
            Status = "Ошибка экспорта: " + ex.Message;
            Serilog.Log.Error(ex, "Export failed");
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void FirstPage() => GoToPage(0);

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void PreviousPage() => GoToPage(Page - 1);

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void NextPage() => GoToPage(Page + 1);

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void LastPage() => GoToPage(PageCount - 1);

    [RelayCommand]
    private void ReloadCatalog() => LoadCatalog();

    [RelayCommand]
    private void CloseTabCommand(ReportTabViewModel? tab) => CloseTab(tab);

    [RelayCommand]
    private void CloseOtherTabsCommand(ReportTabViewModel? tab) => CloseOtherTabs(tab);

    [RelayCommand]
    private void CloseAll() => CloseAllTabs();

    private void NotifyCommandsChanged()
    {
        RunCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
        FirstPageCommand.NotifyCanExecuteChanged();
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        LastPageCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanCancelRun));
    }

    private void NotifyLayoutChanged()
    {
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(PageCaption));
        OnPropertyChanged(nameof(TotalCaption));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(IsReportSelected));
        OnPropertyChanged(nameof(HasListPreview));
        OnPropertyChanged(nameof(SelectedTabColumns));
        OnPropertyChanged(nameof(SelectedTabRows));
        NotifyCommandsChanged();
    }

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is null) return;
        foreach (ReportTabViewModel tab in e.NewItems)
            tab.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(ReportTabViewModel.Rows) or nameof(ReportTabViewModel.PreviewRows)
                    or nameof(ReportTabViewModel.TotalRows) or nameof(ReportTabViewModel.TableRows)
                    or nameof(ReportTabViewModel.HasRows))
                    NotifyLayoutChanged();
            };
    }

    private void ApplyDefaultPeriod()
    {
        var today = DateTime.Today;
        var from = SelectedTab?.Definition.Parameters
            .FirstOrDefault(parameter => parameter.Name == "p_from" && !string.IsNullOrWhiteSpace(parameter.Default))?.Default;
        var to = SelectedTab?.Definition.Parameters
            .FirstOrDefault(parameter => parameter.Name == "p_to" && !string.IsNullOrWhiteSpace(parameter.Default))?.Default;
        SetPeriod(new DateTimeOffset(ResolveDefault(from, today, new DateTime(today.Year, today.Month, 1))),
                  new DateTimeOffset(ResolveDefault(to, today, today)));
    }

    private static DateTimeOffset DefaultFrom()
    {
        var today = DateTime.Today;
        return new DateTimeOffset(new DateTime(today.Year, today.Month, 1));
    }

    private static DateTime ResolveDefault(string? value, DateTime today, DateTime fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        try { return ReportCatalogLoader.ResolveDateDefault(value, DateOnly.FromDateTime(today)).ToDateTime(TimeOnly.MinValue); }
        catch (ArgumentException) { return fallback; }
        catch (FormatException) { return fallback; }
    }

    private void BuildTree(ReportCatalog catalog)
    {
        var folders = new Dictionary<string, CatalogNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in catalog.Folders.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            CatalogNode? parent = null;
            var key = "";
            foreach (var segment in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                if (segment.Length == 0) continue;
                key = key.Length == 0 ? segment : key + "/" + segment;
                if (!folders.TryGetValue(key, out var node))
                {
                    node = new CatalogNode(segment);
                    folders[key] = node;
                    if (parent is null) Catalog.Add(node);
                    else parent.Children.Add(node);
                }
                parent = node;
            }
        }

        foreach (var report in catalog.Reports)
        {
            var folder = FindFolder(report, folders);
            if (folder is null)
            {
                folder = new CatalogNode("Прочее");
                Catalog.Add(folder);
            }
            folder.Children.Add(new CatalogNode(report.Title, report.Code) { Report = report });
        }

        foreach (var node in Catalog.ToList().Where(node => node.Children.Count == 0)) Catalog.Remove(node);
    }

    private static CatalogNode? FindFolder(ReportDefinition report, Dictionary<string, CatalogNode> folders)
    {
        var prefix = report.Code.Split('.')[0];
        return folders.Values.FirstOrDefault(folder => string.Equals(folder.Title, prefix, StringComparison.OrdinalIgnoreCase)
                                                       || report.Code.StartsWith(folder.Title + ".", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        foreach (var tab in Tabs) tab.Dispose();
    }
}
