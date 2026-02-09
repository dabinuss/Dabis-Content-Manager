using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DCM.App.Infrastructure.Converters;

public sealed class IsStringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isString = value is string text && !string.IsNullOrWhiteSpace(text);
        var invert = string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase);
        if (invert)
        {
            isString = !isString;
        }

        return isString ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
