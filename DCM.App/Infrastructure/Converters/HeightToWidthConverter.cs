using System;
using System.Globalization;
using System.Windows.Data;

namespace DCM.App.Infrastructure.Converters;

public sealed class HeightToWidthConverter : IValueConverter
{
    public double Ratio { get; set; } = 16d / 9d;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double height && height > 0)
        {
            return height * Ratio;
        }

        return 0d;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
