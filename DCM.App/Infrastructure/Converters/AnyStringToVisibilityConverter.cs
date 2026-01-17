using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DCM.App.Infrastructure.Converters;

public sealed class AnyStringToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        foreach (var value in values)
        {
            if (value is string text && !string.IsNullOrWhiteSpace(text))
            {
                return Visibility.Visible;
            }
        }

        return Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        Array.Empty<object>();
}
