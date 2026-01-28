using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace DCM.App.Infrastructure.Converters;

public sealed class LanguageCodeToFlagEmojiConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var code = value?.ToString() ?? string.Empty;

        if (Application.Current is null)
        {
            return DependencyProperty.UnsetValue;
        }

        if (code.StartsWith("de", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveFlag("Flag.DE");
        }

        if (code.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveFlag("Flag.US");
        }

        return ResolveFlag("Flag.World");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static object ResolveFlag(string resourceKey)
    {
        var resource = Application.Current?.TryFindResource(resourceKey);
        return resource is ImageSource ? resource : DependencyProperty.UnsetValue;
    }
}
