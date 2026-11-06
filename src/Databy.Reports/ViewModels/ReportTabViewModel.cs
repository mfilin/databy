using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using Databy.Reports.Catalog;
using Databy.Reports.Services;

namespace Databy.Reports.ViewModels;

public sealed partial class ReportTabViewModel : ObservableObject, IDisposable
{
    public ReportTabViewModel(ReportDefinition definition) => Definition = definition;

    public ReportDefinition Definition { get; }
    public string Title => Definition.Title;
    public string Code => Definition.Code;
    public override string ToString() => Title;

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
        var columns = Definition.Result?.Columns ?? new Dictionary<string, ColumnDefinition>();
        var names = CurrentResult?.Preview.Table.Columns.Cast<DataColumn>()
            .Where(column => columns.TryGetValue(column.ColumnName, out var definition) && definition.Hidden is false)
            .Select(column => column.ColumnName)
            .ToArray();
        if (names is null)
        {
            TableColumns.Clear();
            TableRows.Clear();
            return;
        }

        TableColumns.Clear();
        foreach (var name in names)
        {
            var definition = columns.TryGetValue(name, out var found) ? found : new ColumnDefinition();
            TableColumns.Add(new TableColumnViewModel(name, string.IsNullOrWhiteSpace(definition.Title) ? name : definition.Title!, definition.Format));
        }

        TableRows.Clear();
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
