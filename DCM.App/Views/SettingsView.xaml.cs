using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DCM.Core;
using DCM.Core.Configuration;
using DCM.Core.Models;
using DCM.Transcription;

namespace DCM.App.Views;

public partial class SettingsView : UserControl
{
    private bool _transcriptionDownloadButtonAvailable = true;
    private bool _isTranscriptionDownloadBusy;

    public SettingsView()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler? SettingsSaveButtonClicked;
    public event RoutedEventHandler? DefaultVideoFolderBrowseButtonClicked;
    public event RoutedEventHandler? DefaultThumbnailFolderBrowseButtonClicked;
    public event SelectionChangedEventHandler? LanguageComboBoxSelectionChanged;
    public event SelectionChangedEventHandler? ThemeComboBoxSelectionChanged;
    public event RoutedEventHandler? TranscriptionDownloadButtonClicked;
    public event SelectionChangedEventHandler? TranscriptionModelSizeSelectionChanged;
    public event SelectionChangedEventHandler? LlmModeComboBoxSelectionChanged;
    public event RoutedEventHandler? LlmModelPathBrowseButtonClicked;
    public event RoutedPropertyChangedEventHandler<double>? LlmTemperatureSliderValueChanged;

    private void SettingsSaveButton_Click(object sender, RoutedEventArgs e) =>
        SettingsSaveButtonClicked?.Invoke(sender, e);

    private void DefaultVideoFolderBrowseButton_Click(object sender, RoutedEventArgs e) =>
        DefaultVideoFolderBrowseButtonClicked?.Invoke(sender, e);

    private void DefaultThumbnailFolderBrowseButton_Click(object sender, RoutedEventArgs e) =>
        DefaultThumbnailFolderBrowseButtonClicked?.Invoke(sender, e);

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        LanguageComboBoxSelectionChanged?.Invoke(sender, e);

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ThemeComboBoxSelectionChanged?.Invoke(sender, e);

    private void TranscriptionDownloadButton_Click(object sender, RoutedEventArgs e) =>
        TranscriptionDownloadButtonClicked?.Invoke(sender, e);

    private void TranscriptionModelSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        TranscriptionModelSizeSelectionChanged?.Invoke(sender, e);

    private void LlmModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        LlmModeComboBoxSelectionChanged?.Invoke(sender, e);

    private void LlmModelPathBrowseButton_Click(object sender, RoutedEventArgs e) =>
        LlmModelPathBrowseButtonClicked?.Invoke(sender, e);

    private void LlmTemperatureSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LlmTemperatureValueText is not null)
        {
            LlmTemperatureValueText.Text = e.NewValue.ToString("F1", CultureInfo.InvariantCulture);
        }

        LlmTemperatureSliderValueChanged?.Invoke(sender, e);
    }

    public string DefaultVideoFolder
    {
        get => DefaultVideoFolderTextBox.Text ?? string.Empty;
        set => DefaultVideoFolderTextBox.Text = value ?? string.Empty;
    }

    public string DefaultThumbnailFolder
    {
        get => DefaultThumbnailFolderTextBox.Text ?? string.Empty;
        set => DefaultThumbnailFolderTextBox.Text = value ?? string.Empty;
    }

    public bool RememberDraftsBetweenSessions
    {
        get => RememberDraftsCheckBox.IsChecked == true;
        set => RememberDraftsCheckBox.IsChecked = value;
    }

    public bool AutoCleanDrafts
    {
        get => AutoCleanDraftsCheckBox.IsChecked == true;
        set => AutoCleanDraftsCheckBox.IsChecked = value;
    }

    public string DefaultSchedulingTime
    {
        get => DefaultSchedulingTimeTextBox.Text ?? string.Empty;
        set => DefaultSchedulingTimeTextBox.Text = value ?? string.Empty;
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

    public string LlmModelPath
    {
        get => LlmModelPathTextBox.Text ?? string.Empty;
        set => LlmModelPathTextBox.Text = value ?? string.Empty;
    }

    public void SetLanguageOptions(IEnumerable<LanguageInfo> languages, string? selectedCode)
    {
        var languageList = languages?.ToList() ?? new List<LanguageInfo>();
        LanguageComboBox.ItemsSource = languageList;

        if (string.IsNullOrWhiteSpace(selectedCode))
        {
            LanguageComboBox.SelectedItem = languageList.FirstOrDefault();
            return;
        }

        var selected = languageList.FirstOrDefault(l =>
            string.Equals(l.Code, selectedCode, StringComparison.OrdinalIgnoreCase));
        LanguageComboBox.SelectedItem = selected ?? languageList.FirstOrDefault();
    }

    public LanguageInfo? GetSelectedLanguage() => LanguageComboBox.SelectedItem as LanguageInfo;

    public void SetSelectedTheme(string? themeName)
    {
        var target = string.IsNullOrWhiteSpace(themeName) ? "Dark" : themeName.Trim();
        SelectComboBoxItemByTag(ThemeComboBox, target);
    }

    public string GetSelectedTheme()
    {
        if (ThemeComboBox.SelectedItem is ComboBoxItem item)
        {
            var tag = item.Tag?.ToString();
            if (!string.IsNullOrWhiteSpace(tag))
            {
                return tag;
            }

            var content = item.Content?.ToString();
            if (!string.IsNullOrWhiteSpace(content))
            {
                return content;
            }
        }

        return "Dark";
    }

    public void ApplyAppSettings(AppSettings settings)
    {
        DefaultVideoFolder = settings.DefaultVideoFolder ?? string.Empty;
        DefaultThumbnailFolder = settings.DefaultThumbnailFolder ?? string.Empty;
        DefaultSchedulingTime = string.IsNullOrWhiteSpace(settings.DefaultSchedulingTime)
            ? Constants.DefaultSchedulingTime
            : settings.DefaultSchedulingTime;
        TitleSuggestionCount = Math.Max(1, settings.TitleSuggestionCount);
        DescriptionSuggestionCount = Math.Max(1, settings.DescriptionSuggestionCount);
        TagsSuggestionCount = Math.Max(1, settings.TagsSuggestionCount);

        SelectComboBoxItemByTag(DefaultVisibilityComboBox, settings.DefaultVisibility);
        SetSelectedTheme(settings.Theme);

        ConfirmBeforeUploadCheckBox.IsChecked = settings.ConfirmBeforeUpload;
        AutoApplyDefaultTemplateCheckBox.IsChecked = settings.AutoApplyDefaultTemplate;
        OpenBrowserAfterUploadCheckBox.IsChecked = settings.OpenBrowserAfterUpload;
        AutoConnectYouTubeCheckBox.IsChecked = settings.AutoConnectYouTube;
        RememberDraftsBetweenSessions = settings.RememberDraftsBetweenSessions;
        AutoCleanDrafts = settings.AutoRemoveCompletedDrafts;
    }

    public void UpdateAppSettings(AppSettings settings)
    {
        settings.DefaultVideoFolder = string.IsNullOrWhiteSpace(DefaultVideoFolder)
            ? null
            : DefaultVideoFolder.Trim();

        settings.DefaultThumbnailFolder = string.IsNullOrWhiteSpace(DefaultThumbnailFolder)
            ? null
            : DefaultThumbnailFolder.Trim();

        settings.DefaultSchedulingTime = string.IsNullOrWhiteSpace(DefaultSchedulingTime)
            ? null
            : DefaultSchedulingTime.Trim();
        settings.TitleSuggestionCount = TitleSuggestionCount;
        settings.DescriptionSuggestionCount = DescriptionSuggestionCount;
        settings.TagsSuggestionCount = TagsSuggestionCount;
        settings.Theme = GetSelectedTheme();

        settings.DefaultVisibility = GetDefaultVisibility();
        settings.ConfirmBeforeUpload = ConfirmBeforeUploadCheckBox.IsChecked == true;
        settings.AutoApplyDefaultTemplate = AutoApplyDefaultTemplateCheckBox.IsChecked == true;
        settings.OpenBrowserAfterUpload = OpenBrowserAfterUploadCheckBox.IsChecked == true;
        settings.AutoConnectYouTube = AutoConnectYouTubeCheckBox.IsChecked == true;
        settings.RememberDraftsBetweenSessions = RememberDraftsBetweenSessions;
        settings.AutoRemoveCompletedDrafts = AutoCleanDrafts;
    }

    public void ApplyLlmSettings(LlmSettings settings)
    {
        SelectComboBoxItemByTag(LlmModeComboBox, settings.Mode.ToString());
        SelectComboBoxItemByTag(LlmModelTypeComboBox, settings.ModelType.ToString());

        LlmModelPathTextBox.Text = settings.LocalModelPath ?? string.Empty;
        LlmSystemPromptTextBox.Text = settings.SystemPrompt ?? string.Empty;
        LlmMaxTokensTextBox.Text = settings.MaxTokens.ToString();
        LlmTemperatureSlider.Value = settings.Temperature;
        LlmTemperatureValueText.Text = settings.Temperature.ToString("F1", CultureInfo.InvariantCulture);
        LlmTitleCustomPromptTextBox.Text = settings.TitleCustomPrompt ?? string.Empty;
        LlmDescriptionCustomPromptTextBox.Text = settings.DescriptionCustomPrompt ?? string.Empty;
        LlmTagsCustomPromptTextBox.Text = settings.TagsCustomPrompt ?? string.Empty;
    }

    public void UpdateLlmSettings(LlmSettings settings)
    {
        if (LlmModeComboBox.SelectedItem is ComboBoxItem modeItem &&
            modeItem.Tag is string modeTag &&
            Enum.TryParse(modeTag, ignoreCase: true, out LlmMode mode))
        {
            settings.Mode = mode;
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
        LlmModelPathTextBox.IsEnabled = isLocalMode;
        LlmModelPathBrowseButton.IsEnabled = isLocalMode;
    }

    public void SetLlmStatus(string text, Brush brush)
    {
        LlmStatusTextBlock.Text = text;
        LlmStatusTextBlock.Foreground = brush;
    }

    public void ApplyTranscriptionSettings(TranscriptionSettings settings)
    {
        TranscriptionAutoCheckBox.IsChecked = settings.AutoTranscribeOnVideoSelect;
        SelectComboBoxItemByTag(TranscriptionModelSizeComboBox, settings.ModelSize);
        SelectComboBoxItemByTag(TranscriptionLanguageComboBox, settings.Language ?? "auto");
    }

    public void UpdateTranscriptionSettings(TranscriptionSettings settings)
    {
        settings.AutoTranscribeOnVideoSelect = TranscriptionAutoCheckBox.IsChecked == true;

        if (TranscriptionModelSizeComboBox.SelectedItem is ComboBoxItem sizeItem &&
            sizeItem.Tag is WhisperModelSize modelSize)
        {
            settings.ModelSize = modelSize;
        }

        if (TranscriptionLanguageComboBox.SelectedItem is ComboBoxItem langItem &&
            langItem.Tag is string lang)
        {
            settings.Language = lang == "auto" ? null : lang;
        }
    }

    public void SetTranscriptionStatus(string text, Brush brush)
    {
        TranscriptionStatusTextBlock.Text = text;
        TranscriptionStatusTextBlock.Foreground = brush;
    }

    public void SetTranscriptionDownloadState(bool isBusy)
    {
        _isTranscriptionDownloadBusy = isBusy;
        TranscriptionDownloadProgressBar.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        TranscriptionDownloadProgressBar.IsIndeterminate = isBusy;
        if (!isBusy)
        {
            TranscriptionDownloadProgressBar.Value = 0;
        }

        ApplyTranscriptionDownloadButtonState();
    }

    public void UpdateTranscriptionDownloadProgress(double percent)
    {
        TranscriptionDownloadProgressBar.IsIndeterminate = false;
        TranscriptionDownloadProgressBar.Value = percent;
    }

    public void SetTranscriptionDownloadAvailability(bool isEnabled)
    {
        _transcriptionDownloadButtonAvailable = isEnabled;
        ApplyTranscriptionDownloadButtonState();
    }

    public WhisperModelSize GetSelectedTranscriptionModelSize()
    {
        if (TranscriptionModelSizeComboBox.SelectedItem is ComboBoxItem sizeItem &&
            sizeItem.Tag is WhisperModelSize modelSize)
        {
            return modelSize;
        }

        return WhisperModelSize.Small;
    }

    private void ApplyTranscriptionDownloadButtonState()
    {
        var canEnable = _transcriptionDownloadButtonAvailable && !_isTranscriptionDownloadBusy;
        if (TranscriptionDownloadButton.IsEnabled != canEnable)
        {
            TranscriptionDownloadButton.IsEnabled = canEnable;
        }
    }

    private VideoVisibility GetDefaultVisibility()
    {
        if (DefaultVisibilityComboBox.SelectedItem is ComboBoxItem visItem &&
            visItem.Tag is VideoVisibility visibility)
        {
            return visibility;
        }

        return VideoVisibility.Unlisted;
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
        if (comboBox.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Content?.ToString(), out var parsed) &&
            parsed is >= 1 and <= 5)
        {
            return parsed;
        }

        return fallback;
    }

    private static void SetSelectedSuggestionCount(ComboBox comboBox, int value, int fallback)
    {
        var target = Math.Clamp(value, 1, 5);
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (int.TryParse(item.Content?.ToString(), out var parsed) && parsed == target)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        // fallback
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (int.TryParse(item.Content?.ToString(), out var parsed) && parsed == fallback)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }
}
