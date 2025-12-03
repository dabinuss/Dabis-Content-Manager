using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DCM.Core;
using DCM.Core.Configuration;
using DCM.Core.Models;
using DCM.Llm;

using WinForms = System.Windows.Forms;

namespace DCM.App;

public partial class MainWindow
{
    #region Settings Load/Save

    private void LoadSettings()
    {
        try
        {
            _settings = _settingsProvider.Load();
        }
        catch (System.Exception ex)
        {
            _settings = new AppSettings();
            StatusTextBlock.Text = $"Einstellungen konnten nicht geladen werden: {ex.Message}";
        }

        _settings.Persona ??= new ChannelPersona();
        _settings.Llm ??= new LlmSettings();

        ApplySettingsToUi();
    }

    private void SaveSettings()
    {
        try
        {
            _settingsProvider.Save(_settings);
        }
        catch
        {
            // Für jetzt nicht kritisch.
        }
    }

    private void ApplySettingsToUi()
    {
        DefaultVideoFolderTextBox.Text = _settings.DefaultVideoFolder ?? string.Empty;
        DefaultThumbnailFolderTextBox.Text = _settings.DefaultThumbnailFolder ?? string.Empty;

        DefaultSchedulingTimeTextBox.Text = string.IsNullOrWhiteSpace(_settings.DefaultSchedulingTime)
            ? Constants.DefaultSchedulingTime
            : _settings.DefaultSchedulingTime;

        SelectComboBoxItemByTag(DefaultVisibilityComboBox, _settings.DefaultVisibility);

        ConfirmBeforeUploadCheckBox.IsChecked = _settings.ConfirmBeforeUpload;
        AutoApplyDefaultTemplateCheckBox.IsChecked = _settings.AutoApplyDefaultTemplate;
        OpenBrowserAfterUploadCheckBox.IsChecked = _settings.OpenBrowserAfterUpload;
        AutoConnectYouTubeCheckBox.IsChecked = _settings.AutoConnectYouTube;

        var persona = _settings.Persona ?? new ChannelPersona();
        _settings.Persona = persona;

        ChannelPersonaNameTextBox.Text = persona.Name ?? string.Empty;
        ChannelPersonaChannelNameTextBox.Text = persona.ChannelName ?? string.Empty;
        ChannelPersonaLanguageTextBox.Text = persona.Language ?? string.Empty;
        ChannelPersonaToneOfVoiceTextBox.Text = persona.ToneOfVoice ?? string.Empty;
        ChannelPersonaContentTypeTextBox.Text = persona.ContentType ?? string.Empty;
        ChannelPersonaTargetAudienceTextBox.Text = persona.TargetAudience ?? string.Empty;
        ChannelPersonaAdditionalInstructionsTextBox.Text = persona.AdditionalInstructions ?? string.Empty;

        ApplyLlmSettingsToUi();
    }

    private void ApplyLlmSettingsToUi()
    {
        var llm = _settings.Llm ?? new LlmSettings();

        SelectComboBoxItemByTag(LlmModeComboBox, llm.Mode.ToString());

        LlmModelPathTextBox.Text = llm.LocalModelPath ?? string.Empty;
        LlmMaxTokensTextBox.Text = llm.MaxTokens.ToString();
        LlmTemperatureSlider.Value = llm.Temperature;
        LlmTemperatureValueText.Text = llm.Temperature.ToString("F1", CultureInfo.InvariantCulture);

        LlmTitleCustomPromptTextBox.Text = llm.TitleCustomPrompt ?? string.Empty;
        LlmDescriptionCustomPromptTextBox.Text = llm.DescriptionCustomPrompt ?? string.Empty;
        LlmTagsCustomPromptTextBox.Text = llm.TagsCustomPrompt ?? string.Empty;

        UpdateLlmControlsEnabled();
    }

    private static void SelectComboBoxItemByTag<T>(System.Windows.Controls.ComboBox comboBox, T value)
    {
        var items = comboBox.Items.OfType<ComboBoxItem>().ToList();

        foreach (var item in items)
        {
            if (item.Tag is T tag && EqualityComparer<T>.Default.Equals(tag, value))
            {
                comboBox.SelectedItem = item;
                return;
            }

            if (item.Tag is string tagString && value?.ToString() == tagString)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }

    #endregion

    #region App-Einstellungen Events

    private void DefaultVideoFolderBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        string initialPath;

        if (!string.IsNullOrWhiteSpace(_settings.DefaultVideoFolder) &&
            Directory.Exists(_settings.DefaultVideoFolder))
        {
            initialPath = _settings.DefaultVideoFolder;
        }
        else if (!string.IsNullOrWhiteSpace(_settings.LastVideoFolder) &&
                 Directory.Exists(_settings.LastVideoFolder))
        {
            initialPath = _settings.LastVideoFolder;
        }
        else
        {
            initialPath = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        }

        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Standard-Videoordner auswählen",
            SelectedPath = initialPath,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            DefaultVideoFolderTextBox.Text = dialog.SelectedPath;
            _settings.DefaultVideoFolder = dialog.SelectedPath;
            SaveSettings();
            StatusTextBlock.Text = "Standard-Videoordner aktualisiert.";
        }
    }

    private void DefaultThumbnailFolderBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        string initialPath;

        if (!string.IsNullOrWhiteSpace(_settings.DefaultThumbnailFolder) &&
            Directory.Exists(_settings.DefaultThumbnailFolder))
        {
            initialPath = _settings.DefaultThumbnailFolder;
        }
        else if (!string.IsNullOrWhiteSpace(_settings.LastVideoFolder) &&
                 Directory.Exists(_settings.LastVideoFolder))
        {
            initialPath = _settings.LastVideoFolder;
        }
        else
        {
            initialPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        }

        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Standard-Thumbnailordner auswählen",
            SelectedPath = initialPath,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            DefaultThumbnailFolderTextBox.Text = dialog.SelectedPath;
            _settings.DefaultThumbnailFolder = dialog.SelectedPath;
            SaveSettings();
            StatusTextBlock.Text = "Standard-Thumbnailordner aktualisiert.";
        }
    }

    private void AppSettingsSaveButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.DefaultVideoFolder = string.IsNullOrWhiteSpace(DefaultVideoFolderTextBox.Text)
            ? null
            : DefaultVideoFolderTextBox.Text.Trim();

        _settings.DefaultThumbnailFolder = string.IsNullOrWhiteSpace(DefaultThumbnailFolderTextBox.Text)
            ? null
            : DefaultThumbnailFolderTextBox.Text.Trim();

        _settings.DefaultSchedulingTime = string.IsNullOrWhiteSpace(DefaultSchedulingTimeTextBox.Text)
            ? null
            : DefaultSchedulingTimeTextBox.Text.Trim();

        if (DefaultVisibilityComboBox.SelectedItem is ComboBoxItem visItem &&
            visItem.Tag is VideoVisibility visibility)
        {
            _settings.DefaultVisibility = visibility;
        }
        else
        {
            _settings.DefaultVisibility = VideoVisibility.Unlisted;
        }

        _settings.ConfirmBeforeUpload = ConfirmBeforeUploadCheckBox.IsChecked == true;
        _settings.AutoApplyDefaultTemplate = AutoApplyDefaultTemplateCheckBox.IsChecked == true;
        _settings.OpenBrowserAfterUpload = OpenBrowserAfterUploadCheckBox.IsChecked == true;
        _settings.AutoConnectYouTube = AutoConnectYouTubeCheckBox.IsChecked == true;

        SaveSettings();
        StatusTextBlock.Text = "App-Einstellungen gespeichert.";
    }

    #endregion

    #region Kanalprofil Events

    private void ChannelProfileSaveButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.Persona ??= new ChannelPersona();
        var persona = _settings.Persona;

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

        SaveSettings();
        StatusTextBlock.Text = "Kanalprofil gespeichert.";
    }

    #endregion

    #region LLM-Einstellungen Events

    private void LlmModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateLlmControlsEnabled();
    }

    private void LlmTemperatureSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LlmTemperatureValueText is not null)
        {
            LlmTemperatureValueText.Text = e.NewValue.ToString("F1", CultureInfo.InvariantCulture);
        }
    }

    private void LlmModelPathBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "GGUF-Modelldatei auswählen",
            Filter = "GGUF-Modelle|*.gguf|Alle Dateien|*.*"
        };

        if (!string.IsNullOrWhiteSpace(_settings.Llm?.LocalModelPath))
        {
            var dir = Path.GetDirectoryName(_settings.Llm.LocalModelPath);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                dlg.InitialDirectory = dir;
            }
        }

        if (dlg.ShowDialog(this) == true)
        {
            LlmModelPathTextBox.Text = dlg.FileName;
        }
    }

    private void LlmSettingsSaveButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.Llm ??= new LlmSettings();
        var llm = _settings.Llm;

        if (LlmModeComboBox.SelectedItem is ComboBoxItem modeItem &&
            modeItem.Tag is string modeTag)
        {
            if (Enum.TryParse<LlmMode>(modeTag, ignoreCase: true, out var mode))
            {
                llm.Mode = mode;
            }
            else
            {
                llm.Mode = LlmMode.Off;
            }
        }
        else
        {
            llm.Mode = LlmMode.Off;
        }

        llm.LocalModelPath = string.IsNullOrWhiteSpace(LlmModelPathTextBox.Text)
            ? null
            : LlmModelPathTextBox.Text.Trim();

        if (int.TryParse(LlmMaxTokensTextBox.Text, out var maxTokens))
        {
            llm.MaxTokens = Math.Clamp(maxTokens, 64, 1024);
        }
        else
        {
            llm.MaxTokens = 256;
        }

        llm.Temperature = (float)LlmTemperatureSlider.Value;

        llm.TitleCustomPrompt = string.IsNullOrWhiteSpace(LlmTitleCustomPromptTextBox.Text)
            ? null
            : LlmTitleCustomPromptTextBox.Text.Trim();

        llm.DescriptionCustomPrompt = string.IsNullOrWhiteSpace(LlmDescriptionCustomPromptTextBox.Text)
            ? null
            : LlmDescriptionCustomPromptTextBox.Text.Trim();

        llm.TagsCustomPrompt = string.IsNullOrWhiteSpace(LlmTagsCustomPromptTextBox.Text)
            ? null
            : LlmTagsCustomPromptTextBox.Text.Trim();

        SaveSettings();
        RecreateLlmClient();

        StatusTextBlock.Text = "LLM-Einstellungen gespeichert.";
    }

    private void UpdateLlmControlsEnabled()
    {
        var isLocalMode = false;

        if (LlmModeComboBox.SelectedItem is ComboBoxItem modeItem &&
            modeItem.Tag is string modeTag)
        {
            isLocalMode = string.Equals(modeTag, "Local", StringComparison.OrdinalIgnoreCase);
        }

        LlmModelPathTextBox.IsEnabled = isLocalMode;
        LlmModelPathBrowseButton.IsEnabled = isLocalMode;
    }

    private void UpdateLlmStatusText()
    {
        var llm = _settings.Llm ?? new LlmSettings();

        if (llm.IsOff)
        {
            LlmStatusTextBlock.Text = "Deaktiviert";
            LlmStatusTextBlock.Foreground = System.Windows.Media.Brushes.Gray;
            return;
        }

        if (!llm.IsLocalMode)
        {
            LlmStatusTextBlock.Text = "Unbekannter Modus";
            LlmStatusTextBlock.Foreground = System.Windows.Media.Brushes.Orange;
            return;
        }

        if (string.IsNullOrWhiteSpace(llm.LocalModelPath))
        {
            LlmStatusTextBlock.Text = "Kein Modellpfad konfiguriert";
            LlmStatusTextBlock.Foreground = System.Windows.Media.Brushes.Orange;
            return;
        }

        if (!File.Exists(llm.LocalModelPath))
        {
            LlmStatusTextBlock.Text = "Modelldatei nicht gefunden";
            LlmStatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
            return;
        }

        if (_llmClient is LocalLlmClient localClient)
        {
            if (!localClient.IsReady && string.IsNullOrEmpty(localClient.InitializationError))
            {
                var fileName = Path.GetFileName(llm.LocalModelPath);
                LlmStatusTextBlock.Text = $"Konfiguriert: {fileName} (wird bei Nutzung geladen)";
                LlmStatusTextBlock.Foreground = System.Windows.Media.Brushes.DarkGreen;
                return;
            }

            if (localClient.IsReady)
            {
                var fileName = Path.GetFileName(llm.LocalModelPath);
                LlmStatusTextBlock.Text = $"Bereit: {fileName}";
                LlmStatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
            }
            else if (!string.IsNullOrWhiteSpace(localClient.InitializationError))
            {
                LlmStatusTextBlock.Text = $"Fehler: {localClient.InitializationError}";
                LlmStatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
            }
            else
            {
                LlmStatusTextBlock.Text = "Status unbekannt";
                LlmStatusTextBlock.Foreground = System.Windows.Media.Brushes.Orange;
            }
        }
        else
        {
            LlmStatusTextBlock.Text = "Client nicht konfiguriert";
            LlmStatusTextBlock.Foreground = System.Windows.Media.Brushes.Gray;
        }
    }

    #endregion
}