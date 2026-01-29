using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using DCM.Core.Models;

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
            SettingChanged?.Invoke(this, EventArgs.Empty);
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
