using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Databy.Reports.Catalog;
using Databy.Reports.Services;

namespace Databy.Reports.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    public const int PageSize = 100;
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
    private string? _status = "Выберите отчёт и нажмите «Обновить»";

    [ObservableProperty]
    private string _catalogVersion = "не указана";

    [ObservableProperty]
    private string? _catalogError;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _page;

    public bool CanRun => SelectedTab is not null && !IsBusy;
    public bool CanExport => SelectedTab?.CurrentResult is not null && !IsBusy;
    public bool CanGoBack => Page > 0 && !IsBusy;
    public bool CanGoForward => Page < PageCount - 1 && !IsBusy;
    public bool HasData => SelectedTab?.Rows is { Count: > 0 };
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
            if (catalog.Reports.Count > 0 && Tabs.Count == 0)
            {
                OpenReport(catalog.Reports[0]);
                ApplyDefaultPeriod();
            }
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
        return created;
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
        NotifyCommandsChanged();
        NotifyLayoutChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEmptyState));
        NotifyCommandsChanged();
        NotifyLayoutChanged();
    }

    private ReportFilter BuildFilter()
    {
        var from = (FilterFromDate ?? FromDate ?? DefaultFrom()).Date;
        var to = (FilterToDate ?? ToDate ?? DateTimeOffset.Now).Date;
        if (to < from)
            throw new ArgumentException("Дата «по» не может быть раньше даты «с»");
        return new ReportFilter(DateOnly.FromDateTime(from), DateOnly.FromDateTime(to));
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        if (SelectedTab is not { } tab) return;
        _runCts = new CancellationTokenSource();
        IsBusy = true;
        Status = "Выполняется запрос…";
        try
        {
            var result = await _queryService.ExecuteAsync(tab.Definition, BuildFilter(), _runCts.Token);
            tab.Apply(result);
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
        NotifyCommandsChanged();
    }

    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is null) return;
        foreach (ReportTabViewModel tab in e.NewItems)
            tab.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(ReportTabViewModel.Rows) or nameof(ReportTabViewModel.PreviewRows) or nameof(ReportTabViewModel.TotalRows))
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
        var root = new CatalogNode("Отчёты");
        var folders = new Dictionary<string, CatalogNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var report in catalog.Reports)
        {
            var prefix = report.Code.Contains('.') ? report.Code[..report.Code.IndexOf('.')] : "Отчёты";
            if (!folders.TryGetValue(prefix, out var node))
            {
                node = new CatalogNode(prefix);
                folders[prefix] = node;
                root.Children.Add(node);
            }
            node.Children.Add(new CatalogNode(report.Title, report.Code) { Report = report });
        }
        Catalog.Add(root);
    }

    public void Dispose()
    {
        _runCts?.Dispose();
        foreach (var tab in Tabs) tab.Dispose();
    }
}