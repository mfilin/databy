using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Databy.Reports.Catalog;
using Databy.Reports.Input;
using Databy.Reports.Services;

namespace Databy.Reports.ViewModels;

/// <summary>Колонка источника в выпадающем списке сопоставления.</summary>
public sealed record SourceColumnOption(string Name, int? Index)
{
    public override string ToString() => Name;
}

/// <summary>Строка предпросмотра списка.</summary>
public sealed record InputPreviewRowViewModel(int RowId, string[] Cells);

/// <summary>Ошибка разбора для отображения.</summary>
public sealed record InputErrorViewModel(int RowId, string Column, string Reason)
{
    public string Caption => $"Строка {RowId}: {Column} — {Reason}";
}

/// <summary>
/// Панель «Список значений»: вставка/загрузка, сопоставление колонок,
/// фоновый разбор с прогрессом и отменой, предпросмотр и ошибки.
/// </summary>
public sealed partial class ListInputViewModel : ObservableObject, IDisposable
{
    private readonly IClipboardService? _clipboard;
    private readonly IFilePickerService? _files;
    private string? _bufferText;
    private string? _filePath;
    private CancellationTokenSource? _parseCts;

    public ListInputViewModel(ReportDefinition definition, IClipboardService? clipboard = null, IFilePickerService? files = null)
    {
        Definition = definition.Input ?? throw new ArgumentException("У отчёта нет секции input", nameof(definition));
        _clipboard = clipboard;
        _files = files;
        foreach (var column in Definition.Columns)
            Columns.Add(new ColumnMappingViewModel(column));
    }

    public InputDefinition Definition { get; }
    public ObservableCollection<ColumnMappingViewModel> Columns { get; } = [];
    public ObservableCollection<SourceColumnOption> SourceColumns { get; } = [];
    public ObservableCollection<string> PreviewHeaders { get; } = [];
    public ObservableCollection<InputPreviewRowViewModel> PreviewRows { get; } = [];
    public ObservableCollection<InputErrorViewModel> Errors { get; } = [];

    [ObservableProperty]
    private bool _firstRowIsHeader;

    [ObservableProperty]
    private string? _sourceName;

    [ObservableProperty]
    private bool _isParsing;

    [ObservableProperty]
    private long _totalRows;

    [ObservableProperty]
    private long _validRows;

    [ObservableProperty]
    private long _errorRows;

    [ObservableProperty]
    private string? _parseError;

    public InputStage? Stage { get; private set; }

    public bool HasSource => _bufferText is not null || _filePath is not null;
    public bool HasPreview => PreviewHeaders.Count > 0;
    public bool HasErrors => Errors.Count > 0;
    public bool IsReady => Stage is not null && !IsParsing;
    public string Counters => $"Всего: {TotalRows:N0} • корректных: {ValidRows:N0} • с ошибками: {ErrorRows:N0}";

    public event EventHandler? ReadyChanged;

    /// <summary>Последняя запущенная операция переразбора (для детерминизма и тестов).</summary>
    public Task PendingReparse { get; private set; } = Task.CompletedTask;

    partial void OnFirstRowIsHeaderChanged(bool value) => PendingReparse = ReparseAsync();

    partial void OnIsParsingChanged(bool value) => NotifyState();

    partial void OnTotalRowsChanged(long value) => OnPropertyChanged(nameof(Counters));

    partial void OnValidRowsChanged(long value) => OnPropertyChanged(nameof(Counters));

    partial void OnErrorRowsChanged(long value) => OnPropertyChanged(nameof(Counters));

    [RelayCommand]
    private async Task PasteAsync()
    {
        if (_clipboard is null) return;
        var text = await _clipboard.GetTextAsync();
        if (text is null) return;
        _bufferText = text;
        _filePath = null;
        SourceName = "Вставлено из буфера";
        await ReparseAsync();
    }

    [RelayCommand]
    private async Task LoadFileAsync()
    {
        if (_files is null) return;
        var path = await _files.PickInputFileAsync();
        if (path is null) return;
        _filePath = path;
        _bufferText = null;
        SourceName = Path.GetFileName(path);
        await ReparseAsync();
    }

    [RelayCommand]
    private void Clear()
    {
        _bufferText = null;
        _filePath = null;
        SourceName = null;
        ParseError = null;
        CancelParse();
        Stage?.Dispose();
        Stage = null;
        ClearPreview();
        TotalRows = ValidRows = ErrorRows = 0;
        NotifyState();
    }

    /// <summary>Перечитывает источник с текущими опциями.</summary>
    public Task ReparseAsync() => PendingReparse = ReparseCoreAsync();

    private async Task ReparseCoreAsync()
    {
        CancelParse();
        if (!HasSource)
        {
            ClearPreview();
            NotifyState();
            return;
        }

        var cts = new CancellationTokenSource();
        _parseCts = cts;
        IsParsing = true;
        ParseError = null;
        var options = new InputParseOptions
        {
            HasHeader = FirstRowIsHeader,
            Columns = Definition.Columns,
            ColumnMapping = BuildMapping(),
            MaxRows = Definition.MaxRows
        };

        try
        {
            var text = _bufferText;
            var path = _filePath;
            var parser = new ListInputParser();
            var result = await Task.Run(() => text is not null
                ? parser.ParseText(text, options, cts.Token)
                : parser.ParseFile(path!, options, cts.Token), cts.Token).ConfigureAwait(true);

            if (cts.IsCancellationRequested)
            {
                result.Dispose();
                return;
            }

            Stage?.Dispose();
            Stage = result.Stage;
            ApplyPreview(result);
        }
        catch (OperationCanceledException)
        {
            // Новый разбор или «Очистить» уже обновили состояние.
        }
        catch (Exception ex)
        {
            ParseError = ex.Message;
            Stage?.Dispose();
            Stage = null;
            ClearPreview();
        }
        finally
        {
            if (ReferenceEquals(_parseCts, cts))
            {
                _parseCts = null;
                cts.Dispose();
                IsParsing = false;
                NotifyState();
            }
        }
    }

    private void CancelParse()
    {
        var cts = _parseCts;
        _parseCts = null;
        if (cts is null) return;
        cts.Cancel();
        cts.Dispose();
    }

    private void ApplyPreview(InputParseResult result)
    {
        PreviewHeaders.Clear();
        foreach (var name in result.SourceColumns) PreviewHeaders.Add(name);

        SourceColumns.Clear();
        SourceColumns.Add(new SourceColumnOption("— не выбрано —", null));
        for (var i = 0; i < result.SourceColumns.Count; i++)
            SourceColumns.Add(new SourceColumnOption(result.SourceColumns[i], i));
        foreach (var column in Columns) column.UpdateSourceColumns(SourceColumns);

        PreviewRows.Clear();
        foreach (var row in result.Preview)
            PreviewRows.Add(new InputPreviewRowViewModel(row.RowId, row.Cells.ToArray()));

        Errors.Clear();
        foreach (var error in result.Errors)
            Errors.Add(new InputErrorViewModel(error.RowId, error.Column, error.Reason));

        TotalRows = result.TotalRows;
        ValidRows = result.ValidRows;
        ErrorRows = result.ErrorRows;
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(HasErrors));
    }

    private void ClearPreview()
    {
        PreviewHeaders.Clear();
        PreviewRows.Clear();
        Errors.Clear();
        SourceColumns.Clear();
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(HasErrors));
    }

    private Dictionary<string, int> BuildMapping()
    {
        var mapping = new Dictionary<string, int>();
        for (var i = 0; i < Columns.Count; i++)
        {
            var column = Columns[i];
            // По умолчанию — позиционное сопоставление: ключевая колонка i → колонка источника i.
            var index = column.SelectedSource?.Index ?? i;
            mapping[column.Name] = index;
        }
        return mapping;
    }

    /// <summary>Перечитывает источник после смены сопоставления.</summary>
    public Task ReparseForMappingAsync() => ReparseAsync();

    internal void MappingChanged() => _ = ReparseAsync();

    private void NotifyState()
    {
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(HasSource));
        ReadyChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        CancelParse();
        Stage?.Dispose();
        Stage = null;
    }
}

/// <summary>Сопоставление ключевой колонки контракта с колонкой источника.</summary>
public sealed partial class ColumnMappingViewModel : ObservableObject
{
    private bool _suppress;

    public ColumnMappingViewModel(InputColumnDefinition column)
    {
        Name = column.Name;
        Type = column.Type;
        Required = column.Required;
    }

    public string Name { get; }
    public string Type { get; }
    public bool Required { get; }
    public ObservableCollection<SourceColumnOption> SourceColumns { get; } = [];

    [ObservableProperty]
    private SourceColumnOption? _selectedSource;

    public Action? Changed { get; set; }

    partial void OnSelectedSourceChanged(SourceColumnOption? value)
    {
        if (_suppress) return;
        Changed?.Invoke();
    }

    public void UpdateSourceColumns(IEnumerable<SourceColumnOption> options)
    {
        var previous = SelectedSource?.Index;
        _suppress = true;
        try
        {
            SourceColumns.Clear();
            foreach (var option in options) SourceColumns.Add(option);
            SelectedSource = previous is { } index
                ? SourceColumns.FirstOrDefault(option => option.Index == index)
                : SourceColumns.Count > 1 ? SourceColumns[1] : null;
        }
        finally
        {
            _suppress = false;
        }
    }
}
