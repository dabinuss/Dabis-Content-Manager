using System.Windows;

namespace DCM.App.Infrastructure.AttachedProperties;

/// <summary>
/// Attached properties for inline validation feedback on input fields.
/// Shows green checkmark when valid, red X when invalid.
/// </summary>
public static class InputValidation
{
    /// <summary>
    /// Validation state: None (no validation shown), Valid (green check), Invalid (red X)
    /// </summary>
    public static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached(
            "State",
            typeof(ValidationState),
            typeof(InputValidation),
            new PropertyMetadata(ValidationState.None));

    public static void SetState(DependencyObject element, ValidationState value) =>
        element.SetValue(StateProperty, value);

    public static ValidationState GetState(DependencyObject element) =>
        (ValidationState)element.GetValue(StateProperty);

    /// <summary>
    /// Minimum length required for validation to pass (0 = no minimum)
    /// </summary>
    public static readonly DependencyProperty MinLengthProperty =
        DependencyProperty.RegisterAttached(
            "MinLength",
            typeof(int),
            typeof(InputValidation),
            new PropertyMetadata(0));

    public static void SetMinLength(DependencyObject element, int value) =>
        element.SetValue(MinLengthProperty, value);

    public static int GetMinLength(DependencyObject element) =>
        (int)element.GetValue(MinLengthProperty);

    /// <summary>
    /// Whether the field is required (non-empty)
    /// </summary>
    public static readonly DependencyProperty IsRequiredProperty =
        DependencyProperty.RegisterAttached(
            "IsRequired",
            typeof(bool),
            typeof(InputValidation),
            new PropertyMetadata(false));

    public static void SetIsRequired(DependencyObject element, bool value) =>
        element.SetValue(IsRequiredProperty, value);

    public static bool GetIsRequired(DependencyObject element) =>
        (bool)element.GetValue(IsRequiredProperty);

    /// <summary>
    /// Error message to display when validation fails
    /// </summary>
    public static readonly DependencyProperty ErrorMessageProperty =
        DependencyProperty.RegisterAttached(
            "ErrorMessage",
            typeof(string),
            typeof(InputValidation),
            new PropertyMetadata(string.Empty));

    public static void SetErrorMessage(DependencyObject element, string value) =>
        element.SetValue(ErrorMessageProperty, value);

    public static string GetErrorMessage(DependencyObject element) =>
        (string)element.GetValue(ErrorMessageProperty);
}

public enum ValidationState
{
    None,
    Valid,
    Invalid
}
