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

public partial class GeneralSettingsView : UserControl
{
    private bool _transcriptionDownloadButtonAvailable = true;
    private bool _isTranscriptionDownloadBusy;
    private bool _isApplyingSettings;

    public GeneralSettingsView()
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

    public event RoutedEventHandler? SettingsSaveButtonClicked;
    public event RoutedEventHandler? DefaultVideoFolderBrowseButtonClicked;
    public event RoutedEventHandler? DefaultThumbnailFolderBrowseButtonClicked;
    public event SelectionChangedEventHandler? LanguageComboBoxSelectionChanged;
    public event SelectionChangedEventHandler? ThemeComboBoxSelectionChanged;
    public event RoutedEventHandler? TranscriptionDownloadButtonClicked;
    public event SelectionChangedEventHandler? TranscriptionModelSizeSelectionChanged;

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

    private void OnSettingChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isApplyingSettings)
        {
            SettingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnCheckBoxSettingChanged(object sender, RoutedEventArgs e)
    {
        if (!_isApplyingSettings)
        {
            SettingChangedImmediate?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnComboBoxSettingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isApplyingSettings)
        {
            SettingChangedImmediate?.Invoke(this, EventArgs.Empty);
        }
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
        _isApplyingSettings = true;
        try
        {
            DefaultVideoFolder = settings.DefaultVideoFolder ?? string.Empty;
            DefaultThumbnailFolder = settings.DefaultThumbnailFolder ?? string.Empty;
            DefaultSchedulingTime = string.IsNullOrWhiteSpace(settings.DefaultSchedulingTime)
                ? Constants.DefaultSchedulingTime
                : settings.DefaultSchedulingTime;

            SelectComboBoxItemByTag(DefaultVisibilityComboBox, settings.DefaultVisibility);
            SetSelectedTheme(settings.Theme);

            ConfirmBeforeUploadCheckBox.IsChecked = settings.ConfirmBeforeUpload;
            AutoApplyDefaultTemplateCheckBox.IsChecked = settings.AutoApplyDefaultTemplate;
            OpenBrowserAfterUploadCheckBox.IsChecked = settings.OpenBrowserAfterUpload;
            AutoConnectYouTubeCheckBox.IsChecked = settings.AutoConnectYouTube;
            RememberDraftsBetweenSessions = settings.RememberDraftsBetweenSessions;
            AutoCleanDrafts = settings.AutoRemoveCompletedDrafts;
        }
        finally
        {
            _isApplyingSettings = false;
        }
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
        settings.Theme = GetSelectedTheme();

        settings.DefaultVisibility = GetDefaultVisibility();
        settings.ConfirmBeforeUpload = ConfirmBeforeUploadCheckBox.IsChecked == true;
        settings.AutoApplyDefaultTemplate = AutoApplyDefaultTemplateCheckBox.IsChecked == true;
        settings.OpenBrowserAfterUpload = OpenBrowserAfterUploadCheckBox.IsChecked == true;
        settings.AutoConnectYouTube = AutoConnectYouTubeCheckBox.IsChecked == true;
        settings.RememberDraftsBetweenSessions = RememberDraftsBetweenSessions;
        settings.AutoRemoveCompletedDrafts = AutoCleanDrafts;
    }

    public void ApplyTranscriptionSettings(TranscriptionSettings settings)
    {
        _isApplyingSettings = true;
        try
        {
            TranscriptionAutoCheckBox.IsChecked = settings.AutoTranscribeOnVideoSelect;
            SelectComboBoxItemByTag(TranscriptionModelSizeComboBox, settings.ModelSize);
            SelectComboBoxItemByTag(TranscriptionLanguageComboBox, settings.Language ?? "auto");
        }
        finally
        {
            _isApplyingSettings = false;
        }
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
}
