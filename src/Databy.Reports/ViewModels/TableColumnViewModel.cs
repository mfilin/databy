using System.Globalization;
using System.Data;

namespace Databy.Reports.ViewModels;

public sealed class TableColumnViewModel
{
    public TableColumnViewModel(string name, string title, string? format, int? width = null)
    {
        Name = name;
        Title = title;
        Format = format;
        Width = width;
    }

    public string Name { get; }
    public string Title { get; }
    public string? Format { get; }
    public int? Width { get; }
    public string WidthText => Width is null ? "*" : Width.Value.ToString(CultureInfo.InvariantCulture) + "px";

    public string FormatValue(object? value) => value switch
    {
        null or DBNull => string.Empty,
        DateTime date when !string.IsNullOrWhiteSpace(Format) => date.ToString(Format, CultureInfo.CurrentCulture),
        DateOnly date when !string.IsNullOrWhiteSpace(Format) => date.ToString(Format, CultureInfo.CurrentCulture),
        IFormattable formattable when !string.IsNullOrWhiteSpace(Format) => formattable.ToString(Format, CultureInfo.CurrentCulture),
        _ => Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty
    };
}