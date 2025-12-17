using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DCM.Core;
using DCM.Core.Configuration;
using DCM.Core.Models;
using DCM.Llm;

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
        catch (Exception ex)
        {
            _settings = new AppSettings();
            StatusTextBlock.Text = LocalizationHelper.Format("Status.Settings.LoadFailed", ex.Message);
        }

        _settings.Persona ??= new ChannelPersona();
        _settings.Llm ??= new LlmSettings();
        _settings.SavedDrafts ??= new List<UploadDraftSnapshot>();
        _settings.TitleSuggestionCount = Math.Clamp(_settings.TitleSuggestionCount, 1, 5);
        _settings.DescriptionSuggestionCount = Math.Clamp(_settings.DescriptionSuggestionCount, 1, 5);
        _settings.TagsSuggestionCount = Math.Clamp(_settings.TagsSuggestionCount, 1, 5);
        _settings.Theme = string.IsNullOrWhiteSpace(_settings.Theme) ? "Dark" : _settings.Theme.Trim();

        ApplyTheme(_settings.Theme);

        RestoreDraftsFromSettings();
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
        SettingsPageView?.ApplyAppSettings(_settings);

        var persona = _settings.Persona ?? new ChannelPersona();
        _settings.Persona = persona;
        ChannelPageView?.LoadPersona(persona);

        var llm = _settings.Llm ?? new LlmSettings();
        SettingsPageView?.ApplyLlmSettings(llm);
        UpdateLlmControlsEnabled();
        SettingsPageView?.ApplyTranscriptionSettings(_settings.Transcription ?? new TranscriptionSettings());
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

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = LocalizationHelper.Get("Dialog.VideoFolder.Title"),
            InitialDirectory = initialPath
        };

        if (dialog.ShowDialog(this) == true)
        {
            SettingsPageView!.DefaultVideoFolder = dialog.FolderName;
            _settings.DefaultVideoFolder = dialog.FolderName;
            SaveSettings();
            StatusTextBlock.Text = LocalizationHelper.Get("Status.Settings.VideoFolderUpdated");
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

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = LocalizationHelper.Get("Dialog.ThumbnailFolder.Title"),
            InitialDirectory = initialPath
        };

        if (dialog.ShowDialog(this) == true)
        {
            SettingsPageView!.DefaultThumbnailFolder = dialog.FolderName;
            _settings.DefaultThumbnailFolder = dialog.FolderName;
            SaveSettings();
            StatusTextBlock.Text = LocalizationHelper.Get("Status.Settings.ThumbnailFolderUpdated");
        }
    }

    #endregion

    #region Kanalprofil Events

    private void ChannelProfileSaveButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.Persona ??= new ChannelPersona();
        var persona = _settings.Persona;

        ChannelPageView?.UpdatePersona(persona);

        SaveSettings();
        StatusTextBlock.Text = LocalizationHelper.Get("Status.Settings.ChannelProfileSaved");
    }

    #endregion

    #region LLM-Einstellungen Events

    private void LlmModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateLlmControlsEnabled();
    }

    private void LlmModelPathBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = LocalizationHelper.Get("Dialog.LLMModel.Title"),
            Filter = LocalizationHelper.Get("Dialog.LLMModel.Filter")
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
            SettingsPageView!.LlmModelPath = dlg.FileName;
        }
    }

    private void UpdateLlmControlsEnabled()
    {
        var isLocalMode = SettingsPageView?.IsLocalLlmModeSelected() ?? false;
        SettingsPageView?.SetLlmLocalModeControlsEnabled(isLocalMode);
    }

    private void UpdateLlmStatusText()
    {
        var llm = _settings.Llm ?? new LlmSettings();

        if (llm.IsOff)
        {
            SettingsPageView?.SetLlmStatus(LocalizationHelper.Get("Status.LLM.Deactivated"), System.Windows.Media.Brushes.Gray);
            return;
        }

        if (!llm.IsLocalMode)
        {
            SettingsPageView?.SetLlmStatus(LocalizationHelper.Get("Status.LLM.UnknownMode"), System.Windows.Media.Brushes.Orange);
            return;
        }

        if (string.IsNullOrWhiteSpace(llm.LocalModelPath))
        {
            SettingsPageView?.SetLlmStatus(LocalizationHelper.Get("Status.LLM.NoPath"), System.Windows.Media.Brushes.Orange);
            return;
        }

        if (!File.Exists(llm.LocalModelPath))
        {
            SettingsPageView?.SetLlmStatus(LocalizationHelper.Get("Status.LLM.FileMissing"), System.Windows.Media.Brushes.Red);
            return;
        }

        if (_llmClient is LocalLlmClient localClient)
        {
            var modelTypeInfo = GetLocalizedModelTypeName(llm.ModelType);

            if (!localClient.IsReady && string.IsNullOrEmpty(localClient.InitializationError))
            {
                var fileName = Path.GetFileName(llm.LocalModelPath);
                SettingsPageView?.SetLlmStatus(LocalizationHelper.Format("Status.LLM.Configured", fileName, modelTypeInfo), System.Windows.Media.Brushes.DarkGreen);
                return;
            }

            if (localClient.IsReady)
            {
                var fileName = Path.GetFileName(llm.LocalModelPath);
                SettingsPageView?.SetLlmStatus(LocalizationHelper.Format("Status.LLM.Ready", fileName, modelTypeInfo), System.Windows.Media.Brushes.Green);
            }
            else if (!string.IsNullOrWhiteSpace(localClient.InitializationError))
            {
                SettingsPageView?.SetLlmStatus(LocalizationHelper.Format("Status.LLM.Error", localClient.InitializationError), System.Windows.Media.Brushes.Red);
            }
            else
            {
                SettingsPageView?.SetLlmStatus(LocalizationHelper.Get("Status.LLM.Unknown"), System.Windows.Media.Brushes.Orange);
            }
        }
        else
        {
            SettingsPageView?.SetLlmStatus(LocalizationHelper.Get("Status.LLM.ClientMissing"), System.Windows.Media.Brushes.Gray);
        }
    }

    private static string GetLocalizedModelTypeName(LlmModelType modelType) =>
        modelType switch
        {
            LlmModelType.Auto => LocalizationHelper.Get("Settings.LLM.ModelType.Detect"),
            LlmModelType.Phi3 => LocalizationHelper.Get("Settings.LLM.ModelType.Phi3"),
            LlmModelType.Mistral3 => LocalizationHelper.Get("Settings.LLM.ModelType.Mistral3"),
            _ => modelType.ToString()
        };

    #endregion

    private void SettingsSaveButton_Click(object sender, RoutedEventArgs e)
    {
        // App-Einstellungen
        SettingsPageView?.UpdateAppSettings(_settings);

        var selectedLang = SettingsPageView?.GetSelectedLanguage();
        if (selectedLang is not null)
        {
            _settings.Language = selectedLang.Code;
        }

        // LLM-Einstellungen
        _settings.Llm ??= new LlmSettings();
        SettingsPageView?.UpdateLlmSettings(_settings.Llm);

        // Transkriptions-Einstellungen
        SaveTranscriptionSettings();

        ApplyDraftPreferenceSettings();

        // Alles speichern
        SaveSettings();
        RecreateLlmClient();

        StatusTextBlock.Text = LocalizationHelper.Get("Status.Settings.Saved");
    }
}
