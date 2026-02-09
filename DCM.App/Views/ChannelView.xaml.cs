using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DCM.Core.Models;
using DCM.App.Infrastructure.AttachedProperties;

namespace DCM.App.Views;

public partial class ChannelView : UserControl
{
    private bool _isApplyingSettings;

    public ChannelView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Wird ausgelöst wenn sich eine Einstellung ändert (für Auto-Save mit Debounce).
    /// </summary>
    public event EventHandler? SettingChanged;

    /// <summary>
    /// Wird ausgelöst wenn sich eine Einstellung durch Klick ändert (sofortige Speicherung).
    /// </summary>
    public event EventHandler? SettingChangedImmediate;

    public event RoutedEventHandler? ChannelProfileSaveButtonClicked;

    public void SetLanguageOptions(IEnumerable<string> languages)
    {
        ChannelPersonaLanguageTextBox.ItemsSource = languages;
    }

    public void LoadPersona(ChannelPersona persona)
    {
        _isApplyingSettings = true;
        try
        {
            var data = persona ?? new ChannelPersona();
            ChannelPersonaNameTextBox.Text = data.Name ?? string.Empty;
            ChannelPersonaChannelNameTextBox.Text = data.ChannelName ?? string.Empty;
            ChannelPersonaLanguageTextBox.Text = data.Language ?? string.Empty;
            ChannelPersonaToneOfVoiceTextBox.Text = data.ToneOfVoice ?? string.Empty;
            ChannelPersonaContentTypeTextBox.Text = data.ContentType ?? string.Empty;
            ChannelPersonaTargetAudienceTextBox.Text = data.TargetAudience ?? string.Empty;
            ChannelPersonaAdditionalInstructionsTextBox.Text = data.AdditionalInstructions ?? string.Empty;
        }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    public void UpdatePersona(ChannelPersona persona)
    {
        if (persona is null)
        {
            return;
        }

        persona.Name = string.IsNullOrWhiteSpace(ChannelPersonaNameTextBox.Text)
            ? null
            : ChannelPersonaNameTextBox.Text.Trim();

        persona.ChannelName = string.IsNullOrWhiteSpace(ChannelPersonaChannelNameTextBox.Text)
            ? null
            : ChannelPersonaChannelNameTextBox.Text.Trim();

        persona.Language = string.IsNullOrWhiteSpace(ChannelPersonaLanguageTextBox.Text)
            ? null
            : ChannelPersonaLanguageTextBox.Text.Trim();

        persona.ToneOfVoice = string.IsNullOrWhiteSpace(ChannelPersonaToneOfVoiceTextBox.Text)
            ? null
            : ChannelPersonaToneOfVoiceTextBox.Text.Trim();

        persona.ContentType = string.IsNullOrWhiteSpace(ChannelPersonaContentTypeTextBox.Text)
            ? null
            : ChannelPersonaContentTypeTextBox.Text.Trim();

        persona.TargetAudience = string.IsNullOrWhiteSpace(ChannelPersonaTargetAudienceTextBox.Text)
            ? null
            : ChannelPersonaTargetAudienceTextBox.Text.Trim();

        persona.AdditionalInstructions = string.IsNullOrWhiteSpace(ChannelPersonaAdditionalInstructionsTextBox.Text)
            ? null
            : ChannelPersonaAdditionalInstructionsTextBox.Text.Trim();
    }

    private void ChannelProfileSaveButton_Click(object sender, RoutedEventArgs e) =>
        ChannelProfileSaveButtonClicked?.Invoke(sender, e);

    private void OnSettingChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isApplyingSettings)
        {
            // Update inline validation for the changed field
            if (sender is TextBox textBox)
            {
                UpdateValidationState(textBox);
            }

            SettingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Updates the validation state indicator for a TextBox.
    /// Shows green checkmark when valid, red X when invalid.
    /// </summary>
    private void UpdateValidationState(TextBox textBox)
    {
        // Determine which validation icon to update based on the TextBox
        Border? validationBorder = null;
        TextBlock? validationGlyph = null;
        int minLength = 0;
        bool isRequired = false;

        if (textBox == ChannelPersonaNameTextBox)
        {
            validationBorder = NameValidationIcon;
            validationGlyph = NameValidationIconGlyph;
            minLength = 2;
            isRequired = true;
        }
        else if (textBox == ChannelPersonaChannelNameTextBox)
        {
            validationBorder = ChannelNameValidationIcon;
            validationGlyph = ChannelNameValidationIconGlyph;
            minLength = 2;
            isRequired = true;
        }
        else if (textBox == ChannelPersonaToneOfVoiceTextBox)
        {
            validationBorder = ToneValidationIcon;
            validationGlyph = ToneValidationIconGlyph;
            minLength = 2;
        }
        else if (textBox == ChannelPersonaContentTypeTextBox)
        {
            validationBorder = ContentTypeValidationIcon;
            validationGlyph = ContentTypeValidationIconGlyph;
            minLength = 2;
        }
        else if (textBox == ChannelPersonaTargetAudienceTextBox)
        {
            validationBorder = TargetAudienceValidationIcon;
            validationGlyph = TargetAudienceValidationIconGlyph;
            minLength = 2;
        }
        else if (textBox == ChannelPersonaAdditionalInstructionsTextBox)
        {
            validationBorder = AdditionalValidationIcon;
            validationGlyph = AdditionalValidationIconGlyph;
            minLength = 5;
        }

        if (validationBorder == null || validationGlyph == null)
        {
            return;
        }

        var text = textBox.Text?.Trim() ?? string.Empty;
        var isValid = (!isRequired || text.Length > 0) && text.Length >= minLength;

        // Show validation only if user has entered something
        if (text.Length == 0)
        {
            validationBorder.Visibility = Visibility.Collapsed;
            return;
        }

        validationBorder.Visibility = Visibility.Visible;

        if (isValid)
        {
            validationGlyph.Text = "\ue5ca"; // check icon
            validationGlyph.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
        }
        else
        {
            validationGlyph.Text = "\ue5cd"; // close/X icon
            validationGlyph.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        }
    }

    private void OnComboBoxSettingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isApplyingSettings)
        {
            SettingChangedImmediate?.Invoke(this, EventArgs.Empty);
        }
    }
}
