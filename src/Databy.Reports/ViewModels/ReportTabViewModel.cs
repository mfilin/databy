using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using Databy.Reports.Catalog;
using Databy.Reports.Services;

namespace Databy.Reports.ViewModels;

public sealed partial class ReportTabViewModel : ObservableObject, IDisposable
{
    public ReportTabViewModel(ReportDefinition definition, ListInputViewModel? listInput = null)
    {
        Definition = definition;
        ListInput = listInput;
    }

    public ReportDefinition Definition { get; }
    public string Title => Definition.Title;
    public string Code => Definition.Code;
    public override string ToString() => Title;

    /// <summary>Панель списка значений; null у обычных отчётов.</summary>
    public ListInputViewModel? ListInput { get; }

    [ObservableProperty]
    private DataView? _rows;

    [ObservableProperty]
    private int _page;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageCount), nameof(PagerCaption))]
    private long _totalRows;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageCount))]
    private int _previewRows;

    [ObservableProperty]
    private ObservableCollection<TableColumnViewModel> _tableColumns = [];

    [ObservableProperty]
    private ObservableCollection<TableRowViewModel> _tableRows = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotLoading))]
    private bool _isLoading;

    public bool IsNotLoading => !IsLoading;

    public CancellationTokenSource? RunCancellation { get; set; }

    public void CancelRun() => RunCancellation?.Cancel();

    public ReportQueryResult? CurrentResult { get; private set; }
    public DataView? FullRows => CurrentResult?.Preview.Table.DefaultView;

    public int PageCount => Math.Max(1, (int)Math.Ceiling(PreviewRows / (double)MainWindowViewModel.PageSize));
    public string PagerCaption => PageCount == 0 ? "0 из 0" : $"{Page + 1} из {PageCount}";

    public void Apply(ReportQueryResult result)
    {
        CurrentResult?.Dispose();
        CurrentResult = result;
        TotalRows = result.RowCount;
        PreviewRows = result.Preview.Table.Rows.Count;
        Page = 0;
        Rows = GetPageView(MainWindowViewModel.PageSize);
        BuildTable();
        OnPropertyChanged(nameof(PagerCaption));
    }

    public void GoToPage(int page)
    {
        if (PreviewRows == 0) { Page = 0; return; }
        Page = Math.Clamp(page, 0, PageCount - 1);
        OnPropertyChanged(nameof(PagerCaption));
    }

    public DataView? GetPageView(int pageSize)
    {
        var source = CurrentResult?.Preview.Table;
        if (source is null) return null;
        var page = source.Clone();
        foreach (var row in source.Rows.Cast<DataRow>().Skip(Page * pageSize).Take(pageSize))
            page.ImportRow(row);
        var view = page.DefaultView;
        view.AllowNew = false;
        view.AllowEdit = false;
        view.AllowDelete = false;
        return view;
    }

    public void BuildTable()
    {
        TableColumns.Clear();
        TableRows.Clear();

        if (CurrentResult is null)
        {
            OnPropertyChanged(nameof(HasRows));
            return;
        }

        var databaseColumns = CurrentResult.Preview.Table.Columns.Cast<DataColumn>().Select(column => column.ColumnName);
        var columns = ResultColumns.Resolve(databaseColumns, Definition.Result, out var unknownHidden);
        if (unknownHidden.Count > 0)
            Serilog.Log.Warning("Отчёт {ReportCode}: в result.hide указаны отсутствующие колонки: {Columns}",
                Code, string.Join(", ", unknownHidden));

        foreach (var column in columns)
            TableColumns.Add(new TableColumnViewModel(column.Name, column.Title, column.Format));

        if (TableColumns.Count > 0)
        {
            TableRows.Add(TableRowViewModel.Header(TableColumns));
            if (Rows is not null)
                foreach (DataRowView row in Rows)
                    TableRows.Add(TableRowViewModel.From(row, TableColumns));
        }
        OnPropertyChanged(nameof(HasRows));
    }

    public bool HasRows => TableRows.Count > 1;

    public void Reset()
    {
        RunCancellation?.Dispose();
        RunCancellation = null;
        ListInput?.Dispose();
        IsLoading = false;
        CurrentResult?.Dispose();
        CurrentResult = null;
        Rows = null;
        TableColumns.Clear();
        TableRows.Clear();
        PreviewRows = 0;
        TotalRows = 0;
        Page = 0;
    }

    public void Dispose() => Reset();
}
