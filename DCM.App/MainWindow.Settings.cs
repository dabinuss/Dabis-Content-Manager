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
            Title = "Standard-Videoordner auswählen",
            InitialDirectory = initialPath
        };

        if (dialog.ShowDialog(this) == true)
        {
            SettingsPageView!.DefaultVideoFolder = dialog.FolderName;
            _settings.DefaultVideoFolder = dialog.FolderName;
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

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Standard-Thumbnailordner auswählen",
            InitialDirectory = initialPath
        };

        if (dialog.ShowDialog(this) == true)
        {
            SettingsPageView!.DefaultThumbnailFolder = dialog.FolderName;
            _settings.DefaultThumbnailFolder = dialog.FolderName;
            SaveSettings();
            StatusTextBlock.Text = "Standard-Thumbnailordner aktualisiert.";
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
        StatusTextBlock.Text = "Kanalprofil gespeichert.";
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
            SettingsPageView?.SetLlmStatus("Deaktiviert", System.Windows.Media.Brushes.Gray);
            return;
        }

        if (!llm.IsLocalMode)
        {
            SettingsPageView?.SetLlmStatus("Unbekannter Modus", System.Windows.Media.Brushes.Orange);
            return;
        }

        if (string.IsNullOrWhiteSpace(llm.LocalModelPath))
        {
            SettingsPageView?.SetLlmStatus("Kein Modellpfad konfiguriert", System.Windows.Media.Brushes.Orange);
            return;
        }

        if (!File.Exists(llm.LocalModelPath))
        {
            SettingsPageView?.SetLlmStatus("Modelldatei nicht gefunden", System.Windows.Media.Brushes.Red);
            return;
        }

        if (_llmClient is LocalLlmClient localClient)
        {
            var modelTypeInfo = llm.ModelType == LlmModelType.Auto ? "Auto" : llm.ModelType.ToString();

            if (!localClient.IsReady && string.IsNullOrEmpty(localClient.InitializationError))
            {
                var fileName = Path.GetFileName(llm.LocalModelPath);
                SettingsPageView?.SetLlmStatus($"Konfiguriert: {fileName} ({modelTypeInfo})", System.Windows.Media.Brushes.DarkGreen);
                return;
            }

            if (localClient.IsReady)
            {
                var fileName = Path.GetFileName(llm.LocalModelPath);
                SettingsPageView?.SetLlmStatus($"Bereit: {fileName} ({modelTypeInfo})", System.Windows.Media.Brushes.Green);
            }
            else if (!string.IsNullOrWhiteSpace(localClient.InitializationError))
            {
                SettingsPageView?.SetLlmStatus($"Fehler: {localClient.InitializationError}", System.Windows.Media.Brushes.Red);
            }
            else
            {
                SettingsPageView?.SetLlmStatus("Status unbekannt", System.Windows.Media.Brushes.Orange);
            }
        }
        else
        {
            SettingsPageView?.SetLlmStatus("Client nicht konfiguriert", System.Windows.Media.Brushes.Gray);
        }
    }

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

        // Alles speichern
        SaveSettings();
        RecreateLlmClient();

        StatusTextBlock.Text = "Einstellungen gespeichert.";
    }
}
