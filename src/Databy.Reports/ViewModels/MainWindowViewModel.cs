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
    private readonly IReportExportService _exportService;
    private CancellationTokenSource? _runCts;

    public MainWindowViewModel(IReportCatalogService catalogService, IReportQueryService queryService, IReportExportService exportService)
    {
        _catalogService = catalogService;
        _queryService = queryService;
        _exportService = exportService;
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
    public bool CanRun => SelectedTab is not null && !IsBusy;
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
        var created = new ReportTabViewModel(definition);
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
        OnPropertyChanged(nameof(HasColumns));
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(IsReportSelected));
        NotifyCommandsChanged();
        NotifyLayoutChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEmptyState));
        NotifyCommandsChanged();
        NotifyLayoutChanged();
    }

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
    private async Task RunAsync()
    {
        if (SelectedTab is not { } tab) return;
        _runCts = new CancellationTokenSource();
        IsBusy = true;
        Status = "Выполняется запрос…";
        var startedAt = DateTimeOffset.UtcNow;
        ReportQueryResult? result = null;
        var applied = false;
        try
        {
            result = await _queryService.ExecuteAsync(tab.Definition, BuildParameters(tab.Definition), _runCts.Token);
            await EnsureMinimumLoadingTimeAsync(startedAt, _runCts.Token);
            _runCts.Token.ThrowIfCancellationRequested();
            tab.Apply(result);
            applied = true;
            Page = 0;
            Status = $"Показано {tab.PreviewRows} из {tab.TotalRows:N0} строк" +
                     (tab.TotalRows > tab.PreviewRows ? " (превью ограничено)" : string.Empty);
        }
        catch (OperationCanceledException) { Status = "Запуск отменён"; }
        catch (Exception ex)
        {
            Status = "Ошибка отчёта: " + ex.Message;
            Serilog.Log.Error(ex, "Report {ReportCode} failed", tab.Code);
        }
        finally
        {
            if (!applied) result?.Dispose();
            IsBusy = false;
            _runCts?.Dispose();
            _runCts = null;
            OnPropertyChanged(nameof(CanExport));
            NotifyLayoutChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _runCts?.Cancel();

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
        _runCts?.Dispose();
        foreach (var tab in Tabs) tab.Dispose();
    }
}
