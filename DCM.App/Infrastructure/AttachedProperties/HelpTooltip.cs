using System.Windows;

namespace DCM.App.Infrastructure.AttachedProperties;

/// <summary>
/// Attached property for adding help tooltips with question mark icons to input fields.
/// The tooltip appears when hovering over the question mark icon.
/// </summary>
public static class HelpTooltip
{
    /// <summary>
    /// The tooltip text/example to display when hovering over the help icon.
    /// Setting this property enables the help icon visibility.
    /// </summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text",
            typeof(string),
            typeof(HelpTooltip),
            new PropertyMetadata(string.Empty));

    public static void SetText(DependencyObject element, string value) =>
        element.SetValue(TextProperty, value);

    public static string GetText(DependencyObject element) =>
        (string)element.GetValue(TextProperty);

    /// <summary>
    /// Whether to show the help icon. Automatically true when Text is set.
    /// </summary>
    public static readonly DependencyProperty IsVisibleProperty =
        DependencyProperty.RegisterAttached(
            "IsVisible",
            typeof(bool),
            typeof(HelpTooltip),
            new PropertyMetadata(false));

    public static void SetIsVisible(DependencyObject element, bool value) =>
        element.SetValue(IsVisibleProperty, value);

    public static bool GetIsVisible(DependencyObject element) =>
        (bool)element.GetValue(IsVisibleProperty);
}
