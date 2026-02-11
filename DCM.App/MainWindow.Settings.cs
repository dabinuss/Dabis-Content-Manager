using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Threading;
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
        _settings.PendingTranscriptionQueue ??= new List<Guid>();
        _settings.Clipper ??= new ClipperSettings();
        _settings.TitleSuggestionCount = Math.Clamp(_settings.TitleSuggestionCount, 1, 5);
        _settings.DescriptionSuggestionCount = Math.Clamp(_settings.DescriptionSuggestionCount, 1, 5);
        _settings.TagsSuggestionCount = Math.Clamp(_settings.TagsSuggestionCount, 1, 5);
        _settings.Theme = string.IsNullOrWhiteSpace(_settings.Theme) ? "Dark" : _settings.Theme.Trim();

        ApplyTheme(_settings.Theme);
        ApplyCachedYouTubeOptions();

        RestoreDraftsFromSettings();
        ApplySettingsToUi();
    }

    private void SaveSettings(bool forceSync = false)
    {
        var version = Interlocked.Increment(ref _settingsSaveVersion);

        if (forceSync || _isClosing)
        {
            var gateAcquired = false;
            try
            {
                _settingsSaveGate.Wait();
                gateAcquired = true;
                var snapshot = _settings.DeepCopy();
                _settingsProvider.Save(snapshot);
                Interlocked.Exchange(ref _settingsSaveWrittenVersion, version);
            }
            catch
            {
                // Für jetzt nicht kritisch.
            }
            finally
            {
                if (gateAcquired)
                {
                    _settingsSaveGate.Release();
                }
            }

            return;
        }

        _ = Task.Run(async () =>
        {
            await _settingsSaveGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var latestVersion = Volatile.Read(ref _settingsSaveVersion);
                if (version < latestVersion)
                {
                    return;
                }

                var snapshot = _settings.DeepCopy();
                _settingsProvider.Save(snapshot);
                Interlocked.Exchange(ref _settingsSaveWrittenVersion, version);
            }
            catch
            {
                // Für jetzt nicht kritisch.
            }
            finally
            {
                _settingsSaveGate.Release();
            }
        });
    }

    private void ScheduleSettingsSave(bool recreateLlmClient = false)
    {
        if (_isClosing)
        {
            SaveSettings(forceSync: true);
            return;
        }

        _settingsSaveDirty = true;

        // Merken, dass der LLM-Client nach dem Speichern neu erstellt werden soll
        if (recreateLlmClient)
        {
            _llmClientNeedsRecreate = true;
        }

        // Show "Saving..." indicator
        ShowSaveIndicator(isSaving: true);

        if (_settingsSaveTimer is null)
        {
            _settingsSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _settingsSaveTimer.Tick += SettingsSaveTimer_Tick;
        }

        _settingsSaveTimer.Stop();
        _settingsSaveTimer.Start();
    }

    private void SettingsSaveTimer_Tick(object? sender, EventArgs e)
    {
        _settingsSaveTimer?.Stop();

        if (_settingsSaveDirty)
        {
            _settingsSaveDirty = false;
            SaveSettings();

            // Show "Saved" indicator and auto-hide after delay
            ShowSaveIndicator(isSaving: false);

            // LLM-Client neu erstellen wenn nötig (nach dem Speichern)
            if (_llmClientNeedsRecreate)
            {
                _llmClientNeedsRecreate = false;
                RecreateLlmClient();
            }
        }
    }

    #region Save Indicator

    private DispatcherTimer? _saveIndicatorHideTimer;
    private System.Windows.Media.Animation.Storyboard? _pulseStoryboard;

    private void ShowSaveIndicator(bool isSaving)
    {
        Dispatcher.Invoke(() =>
        {
            SaveIndicatorBorder.Visibility = Visibility.Visible;
            SaveIndicatorBorder.Opacity = 1;

            if (isSaving)
            {
                // Show "Saving..." with pulse animation
                SaveIndicatorIcon.Text = "\ue161"; // save icon
                SaveIndicatorIcon.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
                SaveIndicatorText.Text = LocalizationHelper.Get("Status.Saving");
                SaveIndicatorText.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");

                // Start pulse animation (create new storyboard to avoid frozen issue)
                _pulseStoryboard?.Stop(SaveIndicatorBorder);
                _pulseStoryboard = CreatePulseAnimation();
                _pulseStoryboard.Begin(SaveIndicatorBorder, true);

                // Cancel any pending hide timer
                _saveIndicatorHideTimer?.Stop();
            }
            else
            {
                // Stop pulse animation
                _pulseStoryboard?.Stop(SaveIndicatorBorder);

                // Show "Saved ✓" with success color
                SaveIndicatorIcon.Text = "\ue5ca"; // check icon
                SaveIndicatorIcon.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
                SaveIndicatorText.Text = LocalizationHelper.Get("Status.Saved");
                SaveIndicatorText.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");

                // Auto-hide after 3 seconds
                _saveIndicatorHideTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                _saveIndicatorHideTimer.Tick -= SaveIndicatorHideTimer_Tick;
                _saveIndicatorHideTimer.Tick += SaveIndicatorHideTimer_Tick;
                _saveIndicatorHideTimer.Stop();
                _saveIndicatorHideTimer.Start();
            }
        });
    }

    private static System.Windows.Media.Animation.Storyboard CreatePulseAnimation()
    {
        var animation = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 1,
            To = 0.5,
            Duration = TimeSpan.FromSeconds(0.5),
            AutoReverse = true,
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
        };
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(animation,
            new PropertyPath(OpacityProperty));

        var storyboard = new System.Windows.Media.Animation.Storyboard();
        storyboard.Children.Add(animation);
        return storyboard;
    }

    private void SaveIndicatorHideTimer_Tick(object? sender, EventArgs e)
    {
        _saveIndicatorHideTimer?.Stop();

        // Fade out the indicator
        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = TimeSpan.FromSeconds(0.3)
        };
        fadeOut.Completed += (s, args) =>
        {
            SaveIndicatorBorder.Visibility = Visibility.Collapsed;
            SaveIndicatorBorder.Opacity = 1;
        };
        SaveIndicatorBorder.BeginAnimation(OpacityProperty, fadeOut);
    }

    #endregion

    private void ApplySettingsToUi()
    {
        GeneralSettingsPageView?.ApplyAppSettings(_settings);
        LlmSettingsPageView?.ApplySuggestionSettings(_settings);

        var persona = _settings.Persona ?? new ChannelPersona();
        _settings.Persona = persona;
        ChannelProfilePageView?.LoadPersona(persona);
        AccountsPageView?.SetYouTubeAutoConnectState(_settings.AutoConnectYouTube);
        AccountsPageView?.SetYouTubeLastSync(_settings.YouTubeLastSyncUtc);
        AccountsPageView?.SetYouTubeDefaultVisibility(_settings.DefaultVisibility);
        AccountsPageView?.SetYouTubeDefaultPublishTime(string.IsNullOrWhiteSpace(_settings.DefaultSchedulingTime)
            ? Constants.DefaultSchedulingTime
            : _settings.DefaultSchedulingTime);
        AccountsPageView?.SetYouTubeLocale(_settings.YouTubeOptionsLocale);

        var llm = _settings.Llm ?? new LlmSettings();
        LlmSettingsPageView?.ApplyLlmSettings(llm);
        UpdateLlmControlsEnabled();
        var transcriptionSettings = _settings.Transcription ?? new TranscriptionSettings();
        _settings.Transcription = transcriptionSettings;
        GeneralSettingsPageView?.ApplyTranscriptionSettings(transcriptionSettings);
        UploadPageView?.SetAutoTranscribeEnabled(transcriptionSettings.AutoTranscribeOnVideoSelect);
        ClipperPageView?.ApplyClipperSettings(_settings.Clipper ?? new ClipperSettings());
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
            GeneralSettingsPageView!.DefaultVideoFolder = dialog.FolderName;
            _settings.DefaultVideoFolder = dialog.FolderName;
            ScheduleSettingsSave();
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
            GeneralSettingsPageView!.DefaultThumbnailFolder = dialog.FolderName;
            _settings.DefaultThumbnailFolder = dialog.FolderName;
            ScheduleSettingsSave();
            StatusTextBlock.Text = LocalizationHelper.Get("Status.Settings.ThumbnailFolderUpdated");
        }
    }

    #endregion

    #region Kanalprofil Events

    private void ChannelProfileSaveButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.Persona ??= new ChannelPersona();
        var persona = _settings.Persona;

        ChannelProfilePageView?.UpdatePersona(persona);

        SaveSettings();
        StatusTextBlock.Text = LocalizationHelper.Get("Status.Settings.ChannelProfileSaved");
    }

    private void YouTubeAutoConnectToggled(object? sender, bool isEnabled)
    {
        _settings.AutoConnectYouTube = isEnabled;
        GeneralSettingsPageView?.ApplyAppSettings(_settings);
        ScheduleSettingsSave();
        StatusTextBlock.Text = isEnabled
            ? LocalizationHelper.Get("Status.YouTube.AutoConnectEnabled")
            : LocalizationHelper.Get("Status.YouTube.AutoConnectDisabled");
    }

    private void YouTubeDefaultVisibilityComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (AccountsPageView is null)
        {
            return;
        }

        _settings.DefaultVisibility = AccountsPageView.GetYouTubeDefaultVisibility();
        SaveSettings();
    }

    private void YouTubeDefaultPublishTimeTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (AccountsPageView is null)
        {
            return;
        }

        var raw = AccountsPageView.GetYouTubeDefaultPublishTime();
        _settings.DefaultSchedulingTime = string.IsNullOrWhiteSpace(raw)
            ? null
            : raw.Trim();
        ScheduleSettingsSave();
    }

    #endregion

    #region LLM-Einstellungen Events

    private LlmModelManager? _llmModelManager;
    private CancellationTokenSource? _llmDownloadCts;

    private void LlmModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateLlmControlsEnabled();
    }

    private void LlmModelPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateLlmDownloadButtonState();
    }

    private void LlmModelDownloadButton_Click(object sender, RoutedEventArgs e)
    {
        var action = LlmSettingsPageView?.GetDownloadButtonAction();

        switch (action)
        {
            case "Download":
                StartLlmModelDownload();
                break;
            case "Cancel":
                CancelLlmModelDownload();
                break;
            case "Remove":
                RemoveLlmModel();
                break;
        }
    }

    private async void StartLlmModelDownload()
    {
        var preset = LlmSettingsPageView?.GetSelectedPreset() ?? LlmModelPreset.Phi3Mini4kQ4;

        if (preset == LlmModelPreset.Custom)
        {
            return;
        }

        _llmModelManager ??= new LlmModelManager(_llmHttpClient, _logger);

        // Prüfen ob das Modell bereits heruntergeladen wurde
        if (_llmModelManager.CheckAvailability(preset))
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Settings.LLM.Download.AlreadyAvailable");
            LlmSettingsPageView?.SetDownloadButtonState(isDownloading: false, isAvailable: true);

            // Settings aktualisieren und LLM-Client neu erstellen
            _settings.Llm ??= new LlmSettings();
            _settings.Llm.ModelPreset = preset;
            SaveSettings();
            RecreateLlmClient();
            UpdateLlmStatusText();
            return;
        }

        _llmDownloadCts = new CancellationTokenSource();

        LlmSettingsPageView?.SetDownloadButtonState(isDownloading: true, isAvailable: false);
        LlmSettingsPageView?.SetDownloadProgress(0, LocalizationHelper.Get("Settings.LLM.Download.Downloading"));

        var progress = new Progress<LlmModelDownloadProgress>(p =>
        {
            Dispatcher.Invoke(() =>
            {
                if (p.IsError)
                {
                    LlmSettingsPageView?.HideDownloadProgress();
                    LlmSettingsPageView?.SetDownloadButtonState(isDownloading: false, isAvailable: false);
                    StatusTextBlock.Text = p.Message ?? "Download fehlgeschlagen";
                }
                else
                {
                    var sizeInfo = p.TotalBytes > 0
                        ? $"{p.BytesDownloaded / 1024 / 1024} / {p.TotalBytes / 1024 / 1024} MB"
                        : $"{p.BytesDownloaded / 1024 / 1024} MB";
                    LlmSettingsPageView?.SetDownloadProgress(p.Percent, $"{p.Percent:F0}% - {sizeInfo}");
                }
            });
        });

        try
        {
            var success = await _llmModelManager.DownloadAsync(preset, progress, _llmDownloadCts.Token);

            LlmSettingsPageView?.HideDownloadProgress();

            if (success)
            {
                StatusTextBlock.Text = LocalizationHelper.Get("Settings.LLM.Download.Available");
                LlmSettingsPageView?.SetDownloadButtonState(isDownloading: false, isAvailable: true);

                // Settings aktualisieren und LLM-Client neu erstellen
                _settings.Llm ??= new LlmSettings();
                _settings.Llm.ModelPreset = preset;
                SaveSettings();
                RecreateLlmClient();
                UpdateLlmStatusText();
            }
            else
            {
                LlmSettingsPageView?.SetDownloadButtonState(isDownloading: false, isAvailable: false);
            }
        }
        catch (OperationCanceledException)
        {
            LlmSettingsPageView?.HideDownloadProgress();
            LlmSettingsPageView?.SetDownloadButtonState(isDownloading: false, isAvailable: false);
            StatusTextBlock.Text = "Download abgebrochen";
        }
        catch (Exception ex)
        {
            LlmSettingsPageView?.HideDownloadProgress();
            LlmSettingsPageView?.SetDownloadButtonState(isDownloading: false, isAvailable: false);
            StatusTextBlock.Text = $"Download-Fehler: {ex.Message}";
            _logger.Error($"LLM-Download fehlgeschlagen: {ex.Message}", "Settings", ex);
        }
        finally
        {
            _llmDownloadCts?.Dispose();
            _llmDownloadCts = null;
        }
    }

    private void CancelLlmModelDownload()
    {
        _llmDownloadCts?.Cancel();
    }

    private void RemoveLlmModel()
    {
        var preset = LlmSettingsPageView?.GetSelectedPreset() ?? LlmModelPreset.Phi3Mini4kQ4;

        if (preset == LlmModelPreset.Custom)
        {
            return;
        }

        _llmModelManager ??= new LlmModelManager(_llmHttpClient, _logger);

        if (_llmModelManager.RemoveModel(preset))
        {
            LlmSettingsPageView?.SetDownloadButtonState(isDownloading: false, isAvailable: false);
            StatusTextBlock.Text = LocalizationHelper.Get("Settings.LLM.Download.NotAvailable");

            // Settings speichern und LLM-Client neu erstellen
            SaveSettings();
            RecreateLlmClient();
            UpdateLlmStatusText();
        }
    }

    private void UpdateLlmDownloadButtonState()
    {
        var preset = LlmSettingsPageView?.GetSelectedPreset() ?? LlmModelPreset.Phi3Mini4kQ4;

        if (preset == LlmModelPreset.Custom)
        {
            return;
        }

        _llmModelManager ??= new LlmModelManager(_llmHttpClient, _logger);
        var isAvailable = _llmModelManager.CheckAvailability(preset);
        LlmSettingsPageView?.SetDownloadButtonState(isDownloading: false, isAvailable: isAvailable);
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
            LlmSettingsPageView!.LlmModelPath = dlg.FileName;
        }
    }

    private void UpdateLlmControlsEnabled()
    {
        var isLocalMode = LlmSettingsPageView?.IsLocalLlmModeSelected() ?? false;
        LlmSettingsPageView?.SetLlmLocalModeControlsEnabled(isLocalMode);
        UpdateLlmDownloadButtonState();
    }

    private void UpdateLlmStatusText()
    {
        var llm = _settings.Llm ?? new LlmSettings();

        if (llm.IsOff)
        {
            LlmSettingsPageView?.SetLlmStatus(LocalizationHelper.Get("Status.LLM.Deactivated"), System.Windows.Media.Brushes.Gray);
            UpdateLlmHeaderStatus();
            return;
        }

        if (!llm.IsLocalMode)
        {
            LlmSettingsPageView?.SetLlmStatus(LocalizationHelper.Get("Status.LLM.UnknownMode"), System.Windows.Media.Brushes.Orange);
            UpdateLlmHeaderStatus();
            return;
        }

        var effectivePath = llm.GetEffectiveModelPath();

        if (string.IsNullOrWhiteSpace(effectivePath))
        {
            LlmSettingsPageView?.SetLlmStatus(LocalizationHelper.Get("Status.LLM.NoPath"), System.Windows.Media.Brushes.Orange);
            UpdateLlmHeaderStatus();
            return;
        }

        if (!File.Exists(effectivePath))
        {
            LlmSettingsPageView?.SetLlmStatus(LocalizationHelper.Get("Status.LLM.FileMissing"), System.Windows.Media.Brushes.Red);
            UpdateLlmHeaderStatus();
            return;
        }

        if (_llmClient is LocalLlmClient localClient)
        {
            var modelTypeInfo = GetLocalizedModelTypeName(llm.GetEffectiveModelType());

            if (!localClient.IsReady && string.IsNullOrEmpty(localClient.InitializationError))
            {
                var fileName = Path.GetFileName(effectivePath);
                LlmSettingsPageView?.SetLlmStatus(LocalizationHelper.Format("Status.LLM.Configured", fileName, modelTypeInfo), System.Windows.Media.Brushes.DarkGreen);
                UpdateLlmHeaderStatus();
                return;
            }

            if (localClient.IsReady)
            {
                var fileName = Path.GetFileName(effectivePath);
                LlmSettingsPageView?.SetLlmStatus(LocalizationHelper.Format("Status.LLM.Ready", fileName, modelTypeInfo), System.Windows.Media.Brushes.Green);
                UpdateLlmHeaderStatus();
            }
            else if (!string.IsNullOrWhiteSpace(localClient.InitializationError))
            {
                LlmSettingsPageView?.SetLlmStatus(LocalizationHelper.Format("Status.LLM.Error", localClient.InitializationError), System.Windows.Media.Brushes.Red);
                UpdateLlmHeaderStatus();
            }
            else
            {
                LlmSettingsPageView?.SetLlmStatus(LocalizationHelper.Get("Status.LLM.Unknown"), System.Windows.Media.Brushes.Orange);
                UpdateLlmHeaderStatus();
            }
        }
        else
        {
            LlmSettingsPageView?.SetLlmStatus(LocalizationHelper.Get("Status.LLM.ClientMissing"), System.Windows.Media.Brushes.Gray);
            UpdateLlmHeaderStatus();
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
        GeneralSettingsPageView?.UpdateAppSettings(_settings);

        var selectedLang = GeneralSettingsPageView?.GetSelectedLanguage();
        if (selectedLang is not null)
        {
            _settings.Language = selectedLang.Code;
        }

        // LLM-Einstellungen
        _settings.Llm ??= new LlmSettings();
        LlmSettingsPageView?.UpdateLlmSettings(_settings.Llm);
        LlmSettingsPageView?.UpdateSuggestionSettings(_settings);

        // Transkriptions-Einstellungen
        SaveTranscriptionSettings();

        ApplyDraftPreferenceSettings();

        // Alles speichern
        ScheduleSettingsSave();
        RecreateLlmClient();

        StatusTextBlock.Text = LocalizationHelper.Get("Status.Settings.Saved");
    }
}
