using System.Globalization;

namespace Databy.Reports.Input;

/// <summary>Разбор и проверка значения ключевой колонки. Нормализация не выполняется.</summary>
public static class InputValue
{
    private static readonly string[] DateFormats = ["dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd"];

    public static bool TryParse(string value, string type, out object? parsed)
    {
        parsed = null;
        switch (type.ToLowerInvariant())
        {
            case "string":
                parsed = value;
                return true;
            case "int":
                if (int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
                {
                    parsed = integer;
                    return true;
                }
                return false;
            case "decimal":
                var normalized = value.Replace(',', '.');
                if (decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                {
                    parsed = number;
                    return true;
                }
                return false;
            case "date":
                if (DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    parsed = DateOnly.FromDateTime(date);
                    return true;
                }
                return false;
            case "bool":
                switch (value.Trim().ToLowerInvariant())
                {
                    case "true" or "1" or "да": parsed = true; return true;
                    case "false" or "0" or "нет": parsed = false; return true;
                    default: return false;
                }
            default:
                return false;
        }
    }
}
