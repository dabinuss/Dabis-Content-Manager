using System.Globalization;
using System.Windows;

namespace DCM.App;

public static class LocalizationHelper
{
    public static string Get(string resourceKey)
    {
        if (string.IsNullOrWhiteSpace(resourceKey))
        {
            return string.Empty;
        }

        if (Application.Current?.TryFindResource(resourceKey) is string resourceValue)
        {
            return resourceValue;
        }

        return resourceKey;
    }

    public static string Format(string resourceKey, params object[] args)
    {
        var format = Get(resourceKey);
        return args is { Length: > 0 }
            ? string.Format(CultureInfo.CurrentUICulture, format, args)
            : format;
    }
}
