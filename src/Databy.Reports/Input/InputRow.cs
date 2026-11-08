namespace Databy.Reports.Input;

/// <summary>Строка пользовательского списка, прочитанная из stage.</summary>
public sealed record InputRow(int RowId, string? Error, IReadOnlyList<string> Cells);

/// <summary>Ошибка разбора одной строки списка. Значение ячейки не включается.</summary>
public sealed record InputRowError(int RowId, string Column, string Reason);

/// <summary>Строка предпросмотра: номер строки списка и значения исходных колонок.</summary>
public sealed record InputPreviewRow(int RowId, IReadOnlyList<string> Cells);
