namespace XlsxWriters;

/// <summary>Единый интерфейс писателей: stage-CSV + схема колонок → xlsx.</summary>
public interface IXlsxWriter
{
    string Name { get; }

    void Write(IStageReader reader, string stagePath, IReadOnlyList<StageColumn> columns, string targetPath);
}

/// <summary>Общие преобразования stage-значений (как ToCellValue в продукте).</summary>
internal static class StageValues
{
    public static string Sanitize(string value) =>
        value.Length > StageSchema.ExcelTextLimit ? value[..StageSchema.ExcelTextLimit] : value;

    public static bool IsNull(string value) => value == StageSchema.NullMarker;

    public static string SheetName(string? value)
    {
        var cleaned = string.IsNullOrWhiteSpace(value) ? "Отчёт" : value;
        foreach (var invalid in new[] { '\\', '/', '?', '*', '[', ']', ':' }) cleaned = cleaned.Replace(invalid, '_');
        cleaned = cleaned.Trim('\'', ' ');
        if (cleaned.Length == 0) cleaned = "Отчёт";
        return cleaned[..Math.Min(cleaned.Length, 31)];
    }

    public static (bool IsNumber, double Number) ParseNumber(string value) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number)
            ? (true, number)
            : (false, 0);

    public static (bool IsDate, DateTime Value) ParseDate(string value) =>
        DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date)
            ? (true, date)
            : (false, default);
}