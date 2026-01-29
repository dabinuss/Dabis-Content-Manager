using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DCM.Core.Configuration;

namespace DCM.App.Views;

public partial class LlmSettingsView : UserControl
{
    public LlmSettingsView()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler? SettingsSaveButtonClicked;
    public event SelectionChangedEventHandler? LlmModeComboBoxSelectionChanged;
    public event SelectionChangedEventHandler? LlmModelPresetComboBoxSelectionChanged;
    public event RoutedEventHandler? LlmModelPathBrowseButtonClicked;
    public event RoutedEventHandler? LlmModelDownloadButtonClicked;
    public event RoutedPropertyChangedEventHandler<double>? LlmTemperatureSliderValueChanged;

    private void SettingsSaveButton_Click(object sender, RoutedEventArgs e) =>
        SettingsSaveButtonClicked?.Invoke(sender, e);

    private void LlmModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        LlmModeComboBoxSelectionChanged?.Invoke(sender, e);

    private void LlmModelPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateCustomPathVisibility();
        LlmModelPresetComboBoxSelectionChanged?.Invoke(sender, e);
    }

    private void LlmModelPathBrowseButton_Click(object sender, RoutedEventArgs e) =>
        LlmModelPathBrowseButtonClicked?.Invoke(sender, e);

    private void LlmModelDownloadButton_Click(object sender, RoutedEventArgs e) =>
        LlmModelDownloadButtonClicked?.Invoke(sender, e);

    private void LlmTemperatureSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LlmTemperatureValueText is not null)
        {
            LlmTemperatureValueText.Text = e.NewValue.ToString("F1", CultureInfo.InvariantCulture);
        }

        LlmTemperatureSliderValueChanged?.Invoke(sender, e);
    }

    public string LlmModelPath
    {
        get => LlmModelPathTextBox.Text ?? string.Empty;
        set => LlmModelPathTextBox.Text = value ?? string.Empty;
    }

    public int TitleSuggestionCount
    {
        get => GetSelectedSuggestionCount(TitleSuggestionCountComboBox, 5);
        set => SetSelectedSuggestionCount(TitleSuggestionCountComboBox, value, 5);
    }

    public int DescriptionSuggestionCount
    {
        get => GetSelectedSuggestionCount(DescriptionSuggestionCountComboBox, 3);
        set => SetSelectedSuggestionCount(DescriptionSuggestionCountComboBox, value, 3);
    }

    public int TagsSuggestionCount
    {
        get => GetSelectedSuggestionCount(TagsSuggestionCountComboBox, 1);
        set => SetSelectedSuggestionCount(TagsSuggestionCountComboBox, value, 1);
    }

    public void ApplyLlmSettings(LlmSettings settings)
    {
        SelectComboBoxItemByTag(LlmModeComboBox, settings.Mode.ToString());
        SelectComboBoxItemByTag(LlmModelPresetComboBox, settings.ModelPreset.ToString());
        SelectComboBoxItemByTag(LlmModelTypeComboBox, settings.ModelType.ToString());

        LlmModelPathTextBox.Text = settings.LocalModelPath ?? string.Empty;
        LlmSystemPromptTextBox.Text = settings.SystemPrompt ?? string.Empty;
        LlmMaxTokensTextBox.Text = settings.MaxTokens.ToString();
        LlmTemperatureSlider.Value = settings.Temperature;
        LlmTemperatureValueText.Text = settings.Temperature.ToString("F1", CultureInfo.InvariantCulture);
        LlmTitleCustomPromptTextBox.Text = settings.TitleCustomPrompt ?? string.Empty;
        LlmDescriptionCustomPromptTextBox.Text = settings.DescriptionCustomPrompt ?? string.Empty;
        LlmTagsCustomPromptTextBox.Text = settings.TagsCustomPrompt ?? string.Empty;

        UpdateCustomPathVisibility();
    }

    public void ApplySuggestionSettings(AppSettings settings)
    {
        TitleSuggestionCount = Math.Max(1, settings.TitleSuggestionCount);
        DescriptionSuggestionCount = Math.Max(1, settings.DescriptionSuggestionCount);
        TagsSuggestionCount = Math.Max(1, settings.TagsSuggestionCount);
    }

    public void UpdateLlmSettings(LlmSettings settings)
    {
        if (LlmModeComboBox.SelectedItem is ComboBoxItem modeItem &&
            modeItem.Tag is string modeTag &&
            Enum.TryParse(modeTag, ignoreCase: true, out LlmMode mode))
        {
            settings.Mode = mode;
        }

        if (LlmModelPresetComboBox.SelectedItem is ComboBoxItem presetItem &&
            presetItem.Tag is string presetTag &&
            Enum.TryParse(presetTag, ignoreCase: true, out LlmModelPreset preset))
        {
            settings.ModelPreset = preset;
        }

        if (LlmModelTypeComboBox.SelectedItem is ComboBoxItem typeItem &&
            typeItem.Tag is string typeTag &&
            Enum.TryParse(typeTag, ignoreCase: true, out LlmModelType modelType))
        {
            settings.ModelType = modelType;
        }

        settings.LocalModelPath = string.IsNullOrWhiteSpace(LlmModelPathTextBox.Text)
            ? null
            : LlmModelPathTextBox.Text.Trim();

        settings.SystemPrompt = string.IsNullOrWhiteSpace(LlmSystemPromptTextBox.Text)
            ? null
            : LlmSystemPromptTextBox.Text.Trim();

        settings.MaxTokens = int.TryParse(LlmMaxTokensTextBox.Text, out var maxTokens)
            ? Math.Clamp(maxTokens, 64, 1024)
            : 256;

        settings.Temperature = (float)LlmTemperatureSlider.Value;

        settings.TitleCustomPrompt = string.IsNullOrWhiteSpace(LlmTitleCustomPromptTextBox.Text)
            ? null
            : LlmTitleCustomPromptTextBox.Text.Trim();

        settings.DescriptionCustomPrompt = string.IsNullOrWhiteSpace(LlmDescriptionCustomPromptTextBox.Text)
            ? null
            : LlmDescriptionCustomPromptTextBox.Text.Trim();

        settings.TagsCustomPrompt = string.IsNullOrWhiteSpace(LlmTagsCustomPromptTextBox.Text)
            ? null
            : LlmTagsCustomPromptTextBox.Text.Trim();
    }

    public void UpdateSuggestionSettings(AppSettings settings)
    {
        settings.TitleSuggestionCount = TitleSuggestionCount;
        settings.DescriptionSuggestionCount = DescriptionSuggestionCount;
        settings.TagsSuggestionCount = TagsSuggestionCount;
    }

    public bool IsLocalLlmModeSelected()
    {
        if (LlmModeComboBox.SelectedItem is ComboBoxItem modeItem &&
            modeItem.Tag is string modeTag)
        {
            return string.Equals(modeTag, "Local", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    public void SetLlmLocalModeControlsEnabled(bool isLocalMode)
    {
        LlmModelPresetComboBox.IsEnabled = isLocalMode;
        LlmModelDownloadButton.IsEnabled = isLocalMode;
        LlmModelPathTextBox.IsEnabled = isLocalMode;
        LlmModelPathBrowseButton.IsEnabled = isLocalMode;
        LlmModelTypeComboBox.IsEnabled = isLocalMode;
        UpdateCustomPathVisibility();
    }

    public void UpdateCustomPathVisibility()
    {
        var isCustom = IsCustomPresetSelected();
        var visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;

        LlmCustomPathLabel.Visibility = visibility;
        LlmCustomPathPanel.Visibility = visibility;
        LlmModelTypeLabel.Visibility = visibility;
        LlmModelTypeComboBox.Visibility = visibility;

        // Download-Button nur für Presets (nicht Custom) anzeigen
        LlmModelDownloadButton.Visibility = isCustom ? Visibility.Collapsed : Visibility.Visible;
    }

    public bool IsCustomPresetSelected()
    {
        if (LlmModelPresetComboBox.SelectedItem is ComboBoxItem presetItem &&
            presetItem.Tag is string presetTag)
        {
            return string.Equals(presetTag, "Custom", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    public LlmModelPreset GetSelectedPreset()
    {
        if (LlmModelPresetComboBox.SelectedItem is ComboBoxItem presetItem &&
            presetItem.Tag is string presetTag &&
            Enum.TryParse(presetTag, ignoreCase: true, out LlmModelPreset preset))
        {
            return preset;
        }

        return LlmModelPreset.Phi3Mini4kQ4;
    }

    public void SetDownloadProgress(double percent, string? message = null)
    {
        LlmDownloadProgressPanel.Visibility = Visibility.Visible;
        LlmDownloadProgressBar.Value = percent;
        LlmDownloadProgressText.Text = message ?? $"{percent:F0}%";
    }

    public void HideDownloadProgress()
    {
        LlmDownloadProgressPanel.Visibility = Visibility.Collapsed;
    }

    public void SetDownloadButtonState(bool isDownloading, bool isAvailable)
    {
        if (isDownloading)
        {
            LlmModelDownloadButton.Content = FindResource("Settings.LLM.Download.Cancel");
            LlmModelDownloadButton.Tag = "Cancel";
        }
        else if (isAvailable)
        {
            LlmModelDownloadButton.Content = FindResource("Settings.LLM.Download.Remove");
            LlmModelDownloadButton.Tag = "Remove";
        }
        else
        {
            LlmModelDownloadButton.Content = FindResource("Settings.LLM.Download");
            LlmModelDownloadButton.Tag = "Download";
        }
    }

    public string? GetDownloadButtonAction()
    {
        return LlmModelDownloadButton.Tag as string;
    }

    public void SetLlmStatus(string text, Brush brush)
    {
        LlmStatusTextBlock.Text = text;
        LlmStatusTextBlock.Foreground = brush;
    }

    private static void SelectComboBoxItemByTag<T>(ComboBox comboBox, T value)
    {
        if (comboBox is null)
        {
            return;
        }

        foreach (var item in comboBox.Items)
        {
            if (item is ComboBoxItem comboItem)
            {
                if (comboItem.Tag is T tag && EqualityComparer<T>.Default.Equals(tag, value))
                {
                    comboBox.SelectedItem = comboItem;
                    return;
                }

                if (comboItem.Tag is string tagString &&
                    value is string str &&
                    string.Equals(tagString, str, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = comboItem;
                    return;
                }
            }
        }
    }

    private static int GetSelectedSuggestionCount(ComboBox comboBox, int fallback)
    {
        switch (comboBox.SelectedItem)
        {
            case int intValue when intValue is >= 1 and <= 5:
                return intValue;
            case string strValue when int.TryParse(strValue, out var parsed) && parsed is >= 1 and <= 5:
                return parsed;
            case ComboBoxItem item when int.TryParse(item.Content?.ToString(), out var parsed) && parsed is >= 1 and <= 5:
                return parsed;
        }

        return fallback;
    }

    private static void SetSelectedSuggestionCount(ComboBox comboBox, int value, int fallback)
    {
        var target = Math.Clamp(value, 1, 5);
        foreach (var item in comboBox.Items)
        {
            if (item is int intValue && intValue == target)
            {
                comboBox.SelectedItem = item;
                return;
            }

            if (item is string strValue && int.TryParse(strValue, out var parsedString) && parsedString == target)
            {
                comboBox.SelectedItem = item;
                return;
            }

            if (item is ComboBoxItem comboItem &&
                int.TryParse(comboItem.Content?.ToString(), out var parsedItem) &&
                parsedItem == target)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        foreach (var item in comboBox.Items)
        {
            if (item is int intValue && intValue == fallback)
            {
                comboBox.SelectedItem = item;
                return;
            }

            if (item is string strValue && int.TryParse(strValue, out var parsedString) && parsedString == fallback)
            {
                comboBox.SelectedItem = item;
                return;
            }

            if (item is ComboBoxItem comboItem &&
                int.TryParse(comboItem.Content?.ToString(), out var parsedItem) &&
                parsedItem == fallback)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }
}
