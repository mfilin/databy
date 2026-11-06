using System.Globalization;

namespace Databy.Reports.Catalog;

public static class ParameterDefaults
{
    public static object? Resolve(ParameterDefinition parameter, DateOnly today)
    {
        var value = parameter.Default;
        if (string.IsNullOrWhiteSpace(value)) return null;

        try
        {
            return parameter.Type.ToLowerInvariant() switch
            {
                "date" => ReportCatalogLoader.ResolveDateDefault(value, today),
                "int" => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
                "decimal" => decimal.Parse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture),
                "bool" => bool.Parse(value.Trim()),
                "string" => value,
                _ => throw new FormatException($"тип '{parameter.Type}' не поддерживает значения по умолчанию")
            };
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            throw new FormatException($"Некорректный default параметра '{parameter.Name}' ({parameter.Type}): {ex.Message}", ex);
        }
    }
}
