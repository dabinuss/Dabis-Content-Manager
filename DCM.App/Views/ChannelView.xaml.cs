using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using DCM.Core.Models;

namespace DCM.App.Views;

public partial class ChannelView : UserControl
{
    public ChannelView()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler? ChannelProfileSaveButtonClicked;

    public void SetLanguageOptions(IEnumerable<string> languages)
    {
        ChannelPersonaLanguageTextBox.ItemsSource = languages;
    }

    public void LoadPersona(ChannelPersona persona)
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
}
