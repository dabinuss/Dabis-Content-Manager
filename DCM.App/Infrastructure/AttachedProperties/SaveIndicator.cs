using System.Windows;

namespace DCM.App.Infrastructure.AttachedProperties;

/// <summary>
/// Attached property for displaying auto-save status indicator.
/// States: Idle, Saving, Saved, Error
/// </summary>
public static class SaveIndicator
{
    public static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached(
            "State",
            typeof(SaveState),
            typeof(SaveIndicator),
            new PropertyMetadata(SaveState.Idle));

    public static void SetState(DependencyObject element, SaveState value) =>
        element.SetValue(StateProperty, value);

    public static SaveState GetState(DependencyObject element) =>
        (SaveState)element.GetValue(StateProperty);
}

public enum SaveState
{
    Idle,
    Saving,
    Saved,
    Error
}
