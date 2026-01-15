using System;
using System.Globalization;
using System.Windows.Data;

namespace DCM.App.Infrastructure.Converters;

public sealed class ExtractDurationFromStatusConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var start = text.IndexOf('(');
        var end = text.IndexOf(')');
        if (start < 0 || end <= start)
        {
            return null;
        }

        var duration = text.Substring(start + 1, end - start - 1).Trim();
        return string.IsNullOrWhiteSpace(duration) ? null : duration;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
