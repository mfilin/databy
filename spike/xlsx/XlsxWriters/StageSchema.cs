namespace XlsxWriters;

/// <summary>Тип значения stage-колонки (соответствует DataType в продукте).</summary>
public enum StageType { Int, Text, Decimal, Date, DateTime, Bool }

/// <summary>Описание одной колонки stage-файла.</summary>
public sealed record StageColumn(string Name, string Title, StageType Type, string? Format);

/// <summary>
/// Схема stage-файла для спайка: 12 колонок, соответствующих постановке TASK-013.
/// Порядок колонок — «как из БД», заголовки — как приходят в stage (имена колонок).
/// </summary>
public static class StageSchema
{
    public const string NullMarker = "\\N";
    public const int ExcelTextLimit = 32_767;

    public static readonly StageColumn[] Columns =
    [
        new("id", "ID", StageType.Int, "#,##0"),
        new("short_text", "Короткий текст", StageType.Text, null),
        new("long_cyrillic", "Длинный текст", StageType.Text, null),
        new("amount", "Сумма", StageType.Decimal, "#,##0.00"),
        new("date", "Дата", StageType.Date, "dd.MM.yyyy"),
        new("date_time", "Дата и время", StageType.DateTime, "yyyy-mm-dd hh:mm:ss"),
        new("flag", "Флаг", StageType.Bool, null),
        new("null_text", "NULL текст", StageType.Text, null),
        new("null_int", "NULL число", StageType.Int, "#,##0"),
        new("null_decimal", "NULL дробное", StageType.Decimal, "#,##0.00"),
        new("tail_text", "Хвостовой текст", StageType.Text, null),
        new("tail_int", "Хвостовое число", StageType.Int, "#,##0")
    ];
}