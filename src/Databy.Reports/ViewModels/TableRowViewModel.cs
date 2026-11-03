using System.Data;

namespace Databy.Reports.ViewModels;

public sealed class TableRowViewModel
{
    private readonly string[] _cells;

    private TableRowViewModel(bool isHeader, string[] cells)
    {
        IsHeader = isHeader;
        _cells = cells;
    }

    public static TableRowViewModel Header(IReadOnlyList<TableColumnViewModel> columns) =>
        new(true, columns.Select(column => column.Title).ToArray());

    public static TableRowViewModel From(DataRowView row, IReadOnlyList<TableColumnViewModel> columns) =>
        new(false, columns.Select(column => column.FormatValue(row[column.Name])).ToArray());

    public bool IsHeader { get; }
    public string[] Cells => _cells;
    public string this[int index] => _cells[index];
}