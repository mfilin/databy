using System.Globalization;
using Avalonia.Data.Converters;

namespace Databy.Reports.Converters;

public sealed class DateTimeOffsetToDateTimeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTimeOffset dto)
            return dto.DateTime;
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTime dt)
            return new DateTimeOffset(dt);
        return null;
    }
}
