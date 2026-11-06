using System.Globalization;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Databy.Reports.ViewModels;

public sealed partial class TableColumnViewModel : ObservableObject
{
    public const double DefaultWidth = 160;
    public const double MinimumWidth = 48;

    public TableColumnViewModel(string name, string title, string? format, int? width = null)
    {
        Name = name;
        Title = title;
        Format = format;
        _width = width is { } pixel ? pixel : DefaultWidth;
    }

    public string Name { get; }
    public string Title { get; }
    public string? Format { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WidthText))]
    private double _width;

    public string WidthText => Width.ToString(CultureInfo.InvariantCulture) + "px";

    public void Resize(double width) => Width = Math.Max(MinimumWidth, Math.Round(width));

    public string FormatValue(object? value) => value switch
    {
        null or DBNull => string.Empty,
        DateTime date when !string.IsNullOrWhiteSpace(Format) => date.ToString(Format, CultureInfo.CurrentCulture),
        DateOnly date when !string.IsNullOrWhiteSpace(Format) => date.ToString(Format, CultureInfo.CurrentCulture),
        IFormattable formattable when !string.IsNullOrWhiteSpace(Format) => formattable.ToString(Format, CultureInfo.CurrentCulture),
        _ => Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty
    };
}
