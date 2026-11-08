namespace Databy.Reports.Catalog;

/// <summary>
/// Описание входного списка значений («отчёт по списку», раздел 18 ТЗ).
/// Отчёт без секции <c>input</c> работает как обычный отчёт.
/// </summary>
public sealed class InputDefinition
{
    public const int MaximumRows = 1_000_000;
    public const int DefaultBatchSize = 100_000;
    public const string DefaultLabel = "Список значений";
    public const string DefaultStatusColumn = "Статус поиска";

    public string Label { get; set; } = DefaultLabel;
    public int MaxRows { get; set; } = MaximumRows;
    public int BatchSize { get; set; } = DefaultBatchSize;
    public string RowIdParameter { get; set; } = "";
    public string ResultKey { get; set; } = "";
    public string StatusColumn { get; set; } = DefaultStatusColumn;
    public List<InputColumnDefinition> Columns { get; set; } = [];
}

/// <summary>Колонка входного списка.</summary>
public sealed class InputColumnDefinition
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Label { get; set; } = "";
    public bool Required { get; set; } = true;
}
