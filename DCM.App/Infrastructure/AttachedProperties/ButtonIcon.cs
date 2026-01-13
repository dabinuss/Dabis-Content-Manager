using System.Windows;

namespace DCM.App.Infrastructure.AttachedProperties;

public static class ButtonIcon
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.RegisterAttached(
            "Glyph",
            typeof(string),
            typeof(ButtonIcon),
            new PropertyMetadata("\ue5d5"));

    public static void SetGlyph(DependencyObject element, string value) =>
        element.SetValue(GlyphProperty, value);

    public static string GetGlyph(DependencyObject element) =>
        (string)element.GetValue(GlyphProperty);
}
