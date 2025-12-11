using System.Collections.Generic;
using System.IO;
using System.Windows;
using DCM.Core;
using DCM.Core.Configuration;
using DCM.Transcription;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace DCM.App;

public partial class MainWindow
{
    private const string TranscriptionLogSource = "Transcription";
    private ITranscriptionService? _transcriptionService;
    private CancellationTokenSource? _transcriptionCts;
    private bool _isTranscribing;

    #region Transcription Initialization

    private async Task InitializeTranscriptionServiceAsync()
    {
        try
        {
            var service = await Task.Run(() => new TranscriptionService());
            _transcriptionService = service;
            _logger.Debug(LocalizationHelper.Get("Log.Transcription.ServiceInitialized"), TranscriptionLogSource);
            UpdateTranscriptionButtonState();
            UpdateTranscriptionStatusDisplay();
        }
        catch (Exception ex)
        {
            _logger.Error(LocalizationHelper.Format("Log.Transcription.ServiceInitFailed", ex.Message), TranscriptionLogSource, ex);
            _transcriptionService = null;
            UpdateTranscriptionStatusDisplay();
        }
    }

    private void DisposeTranscriptionService()
    {
        CancelTranscription();

        // Temporäre Dateien aufräumen
        CleanupTranscriptionTempFolder();

        if (_transcriptionService is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch
            {
                // Ignorieren
            }
        }

        _transcriptionService = null;
    }

    private static void CleanupTranscriptionTempFolder()
    {
        try
        {
            var tempFolder = Constants.TranscriptionTempFolder;
            if (Directory.Exists(tempFolder))
            {
                var files = Directory.GetFiles(tempFolder, "*.wav");
                foreach (var file in files)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // Einzelne Dateien ignorieren
                    }
                }
            }
        }
        catch
        {
            // Cleanup-Fehler ignorieren
        }
    }

    #endregion

    #region Transcription UI Events

    private async void TranscribeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isTranscribing)
        {
            CancelTranscription();
            return;
        }

        var videoPath = UploadView.VideoPathTextBox.Text;

        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Status.Transcription.SelectVideo");
            return;
        }

        await StartTranscriptionAsync(videoPath);
    }

    private async Task StartTranscriptionAsync(string videoFilePath)
    {
        if (_transcriptionService is null)
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Status.Transcription.ServiceUnavailable");
            return;
        }

        _isTranscribing = true;
        _transcriptionCts = new CancellationTokenSource();
        var cancellationToken = _transcriptionCts.Token;

        UpdateTranscriptionButtonState();
        ShowTranscriptionProgress();

        _logger.Info(
            LocalizationHelper.Format("Log.Transcription.Started", Path.GetFileName(videoFilePath)),
            TranscriptionLogSource);

        try
        {
            // Dependencies sicherstellen
            var modelSize = _settings.Transcription?.ModelSize ?? WhisperModelSize.Small;

            if (!_transcriptionService.IsReady)
            {
                UpdateTranscriptionPhaseText(LocalizationHelper.Get("Transcription.Phase.LoadingDependencies"));
                StatusTextBlock.Text = LocalizationHelper.Get("Status.Transcription.LoadDependencies");

                var dependencyProgress = new Progress<DependencyDownloadProgress>(ReportDependencyProgress);
                var dependenciesReady = await _transcriptionService.EnsureDependenciesAsync(
                    modelSize,
                    dependencyProgress,
                    cancellationToken);

                if (!dependenciesReady)
                {
                    StatusTextBlock.Text = LocalizationHelper.Get("Status.Transcription.DependenciesFailed");
                    _logger.Warning(LocalizationHelper.Get("Log.Transcription.DependenciesMissing"), TranscriptionLogSource);
                    return;
                }
            }

            // Transkription starten
            var language = _settings.Transcription?.Language;
            var transcriptionProgress = new Progress<TranscriptionProgress>(ReportTranscriptionProgress);

            var result = await _transcriptionService.TranscribeAsync(
                videoFilePath,
                language,
                transcriptionProgress,
                cancellationToken);

            // Ergebnis verarbeiten
            OnTranscriptionCompleted(result);
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Status.Transcription.Canceled");
            UpdateTranscriptionPhaseText(LocalizationHelper.Get("Transcription.Phase.Canceled"));
            _logger.Debug(LocalizationHelper.Get("Log.Transcription.Canceled"), TranscriptionLogSource);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = LocalizationHelper.Format("Status.Transcription.Error", ex.Message);
            UpdateTranscriptionPhaseText(LocalizationHelper.Format("Transcription.Phase.Error", ex.Message));
            _logger.Error(LocalizationHelper.Format("Log.Transcription.Failed", ex.Message), TranscriptionLogSource, ex);
        }
        finally
        {
            _isTranscribing = false;
            _transcriptionCts?.Dispose();
            _transcriptionCts = null;

            HideTranscriptionProgress();
            UpdateTranscriptionButtonState();
            UpdateLogLinkIndicator();
        }
    }

    private void CancelTranscription()
    {
        if (_transcriptionCts is null || !_isTranscribing)
        {
            return;
        }

        try
        {
            _transcriptionCts.Cancel();
            UpdateTranscriptionPhaseText(LocalizationHelper.Get("Transcription.Phase.Canceling"));
            _logger.Debug(LocalizationHelper.Get("Log.Transcription.Canceling"), TranscriptionLogSource);
        }
        catch
        {
            // Ignorieren
        }
    }

    private void OnTranscriptionCompleted(TranscriptionResult result)
    {
        if (result.Success && !string.IsNullOrWhiteSpace(result.Text))
        {
            UploadView.TranscriptTextBox.Text = result.Text;
            StatusTextBlock.Text = LocalizationHelper.Format(
                "Status.Transcription.Completed",
                result.Duration.TotalSeconds);
            UpdateTranscriptionPhaseText(LocalizationHelper.Format("Transcription.Phase.CompletedIn", result.Duration.TotalSeconds));
            _logger.Info(
                LocalizationHelper.Format("Log.Transcription.Success", result.Text.Length, result.Duration.TotalSeconds),
                TranscriptionLogSource);
        }
        else
        {
            var errorText = result.ErrorMessage ?? string.Empty;
            StatusTextBlock.Text = LocalizationHelper.Format(
                "Status.Transcription.ResultFailed",
                errorText);
            UpdateTranscriptionPhaseText(result.ErrorMessage ?? LocalizationHelper.Get("Transcription.Phase.Failed"));
            _logger.Warning(
                LocalizationHelper.Format("Log.Transcription.Failed", result.ErrorMessage ?? string.Empty),
                TranscriptionLogSource);
        }
    }

    #endregion

    #region Transcription Progress UI

    private void ShowTranscriptionProgress()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(ShowTranscriptionProgress);
            return;
        }

        UploadView.TranscriptionProgressBar.Visibility = Visibility.Visible;
        UploadView.TranscriptionProgressBar.IsIndeterminate = true;
        UploadView.TranscriptionProgressBar.Value = 0;
        UploadView.TranscriptionPhaseTextBlock.Visibility = Visibility.Visible;
        UpdateTranscriptionPhaseText(LocalizationHelper.Get("Transcription.Phase.Initializing"));
    }

    private void HideTranscriptionProgress()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(HideTranscriptionProgress);
            return;
        }

        UploadView.TranscriptionProgressBar.Visibility = Visibility.Collapsed;
        UploadView.TranscriptionProgressBar.IsIndeterminate = false;
        UploadView.TranscriptionProgressBar.Value = 0;

        // Phase-Text nach kurzer Zeit ausblenden
        Task.Delay(3000).ContinueWith(_ =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (!_isTranscribing)
                {
                    UploadView.TranscriptionPhaseTextBlock.Visibility = Visibility.Collapsed;
                }
            });
        });
    }

    private void UpdateTranscriptionPhaseText(string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => UpdateTranscriptionPhaseText(text));
            return;
        }

        UploadView.TranscriptionPhaseTextBlock.Text = text;
        UploadView.TranscriptionPhaseTextBlock.Visibility = Visibility.Visible;
    }

    private void ReportDependencyProgress(DependencyDownloadProgress progress)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ReportDependencyProgress(progress));
            return;
        }

        // Alle UI-Updates gebündelt
        UploadView.TranscriptionProgressBar.IsIndeterminate = false;
        UploadView.TranscriptionProgressBar.Value = progress.Percent;

        var typeText = progress.Type == DependencyType.FFmpeg
            ? LocalizationHelper.Get("Transcription.Dependency.Type.FFmpeg")
            : LocalizationHelper.Get("Transcription.Dependency.Type.Whisper");
        var message = LocalizationHelper.Format("Transcription.Dependency.Progress.Percent", typeText, progress.Percent);

        if (progress.TotalBytes > 0 && progress.BytesDownloaded > 0)
        {
            var downloadedMB = progress.BytesDownloaded / (1024.0 * 1024.0);
            var totalMB = progress.TotalBytes / (1024.0 * 1024.0);
            message = LocalizationHelper.Format("Transcription.Dependency.Progress.Bytes", typeText, downloadedMB, totalMB);
        }

        UploadView.TranscriptionPhaseTextBlock.Text = message;
        UploadView.TranscriptionPhaseTextBlock.Visibility = Visibility.Visible;
        StatusTextBlock.Text = progress.Message ?? message;
    }

    private void ReportTranscriptionProgress(TranscriptionProgress progress)
    {
        // Progress<T> wird bereits auf dem UI-Thread aufgerufen,
        // daher ist CheckAccess normalerweise nicht nötig.
        // Zur Sicherheit behalten wir es bei, optimieren aber die Logik.
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ReportTranscriptionProgress(progress));
            return;
        }

        // Alle UI-Updates hier sind jetzt sicher auf dem UI-Thread
        UploadView.TranscriptionProgressBar.IsIndeterminate = progress.Phase == TranscriptionPhase.Initializing;
        UploadView.TranscriptionProgressBar.Value = progress.Percent;

        var phaseText = progress.Phase switch
        {
            TranscriptionPhase.Initializing => LocalizationHelper.Get("Transcription.Phase.Initializing"),
            TranscriptionPhase.ExtractingAudio => LocalizationHelper.Format("Transcription.Phase.ExtractingAudio", progress.Percent),
            TranscriptionPhase.Transcribing => FormatTranscribingProgress(progress),
            TranscriptionPhase.Completed => LocalizationHelper.Get("Transcription.Phase.Completed"),
            TranscriptionPhase.Failed => progress.Message ?? LocalizationHelper.Get("Transcription.Phase.Failed"),
            _ => progress.Message ?? progress.Phase.ToString()
        };

        UploadView.TranscriptionPhaseTextBlock.Text = phaseText;
        UploadView.TranscriptionPhaseTextBlock.Visibility = Visibility.Visible;
        StatusTextBlock.Text = progress.Message ?? phaseText;
    }

    private static string FormatTranscribingProgress(TranscriptionProgress progress)
    {
        var text = LocalizationHelper.Format("Transcription.Progress.Transcribing", progress.Percent);

        if (progress.EstimatedTimeRemaining.HasValue && progress.EstimatedTimeRemaining.Value.TotalSeconds > 0)
        {
            var remaining = progress.EstimatedTimeRemaining.Value;
            if (remaining.TotalMinutes >= 1)
            {
                text += LocalizationHelper.Format("Transcription.Progress.TimeMinutes", remaining.TotalMinutes);
            }
            else
            {
                text += LocalizationHelper.Format("Transcription.Progress.TimeSeconds", remaining.TotalSeconds);
            }
        }

        return text;
    }

    // Allgemeine Funktion zum Umschalten von Button-States
    private void SetButtonState(Button button, bool isActive)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SetButtonState(button, isActive));
            return;
        }

        button.Tag = isActive ? "active" : "default";
    }

    // Optional: Mit Enable/Disable
    private void SetButtonState(Button button, bool isActive, bool isEnabled)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SetButtonState(button, isActive, isEnabled));
            return;
        }

        button.Tag = isActive ? "active" : "default";
        button.IsEnabled = isEnabled;
    }

    private void UpdateTranscriptionButtonState()
    {
        var hasVideo = !string.IsNullOrWhiteSpace(UploadView.VideoPathTextBox.Text);
        SetButtonState(UploadView.TranscribeButton, _isTranscribing, _isTranscribing || hasVideo);
    }

    #endregion

    #region Auto-Transcription

    private async Task TryAutoTranscribeAsync(string videoFilePath)
    {
        // Kleine Verzögerung um UI-Thread Zeit zu geben
        await Task.Delay(100);

        if (_transcriptionService is null)
        {
            return;
        }

        if (_settings.Transcription?.AutoTranscribeOnVideoSelect != true)
        {
            return;
        }

        if (!_transcriptionService.IsReady)
        {
            _logger.Debug(LocalizationHelper.Get("Log.Transcription.AutoSkip"), TranscriptionLogSource);
            return;
        }

        // Prüfung muss auf UI-Thread sein
        if (!Dispatcher.CheckAccess())
        {
            await Dispatcher.InvokeAsync(async () => await TryAutoTranscribeAsync(videoFilePath));
            return;
        }

        // Nur wenn das Transkript-Feld leer ist
        if (!string.IsNullOrWhiteSpace(UploadView.TranscriptTextBox.Text))
        {
            return;
        }

        _logger.Debug(LocalizationHelper.Get("Log.Transcription.AutoStarted"), TranscriptionLogSource);
        await StartTranscriptionAsync(videoFilePath);
    }

    #endregion

    #region Transcription Settings UI

    private void SaveTranscriptionSettings()
    {
        _settings.Transcription ??= new TranscriptionSettings();
        SettingsPageView?.UpdateTranscriptionSettings(_settings.Transcription);
        SaveSettings();
    }

    private void UpdateTranscriptionStatusDisplay()
    {
        if (_transcriptionService is null)
        {
            SettingsPageView?.SetTranscriptionStatus(LocalizationHelper.Get("Transcription.Status.ServiceUnavailable"), System.Windows.Media.Brushes.Red);
            SettingsPageView?.SetTranscriptionDownloadAvailability(false);
            return;
        }

        var status = _transcriptionService.GetDependencyStatus();

        if (status.AllAvailable)
        {
            var modelName = status.InstalledModelSize?.ToString() ?? LocalizationHelper.Get("Common.Unknown");
            SettingsPageView?.SetTranscriptionStatus(LocalizationHelper.Format("Transcription.Status.Ready", modelName), System.Windows.Media.Brushes.Green);
        }
        else
        {
            var missing = new List<string>();
            if (!status.FFmpegAvailable) missing.Add(LocalizationHelper.Get("Transcription.Dependency.Type.FFmpeg"));
            if (!status.WhisperModelAvailable) missing.Add(LocalizationHelper.Get("Transcription.Dependency.Type.Whisper"));

            SettingsPageView?.SetTranscriptionStatus(LocalizationHelper.Format("Transcription.Status.Missing", string.Join(", ", missing)), System.Windows.Media.Brushes.Orange);
        }

        UpdateTranscriptionDownloadAvailability(status);
    }

    private void UpdateTranscriptionDownloadAvailability()
    {
        if (SettingsPageView is null)
        {
            return;
        }

        var status = _transcriptionService?.GetDependencyStatus() ?? DependencyStatus.None;
        UpdateTranscriptionDownloadAvailability(status);
    }

    private void UpdateTranscriptionDownloadAvailability(DependencyStatus status)
    {
        if (SettingsPageView is null)
        {
            return;
        }

        var selectedSize = SettingsPageView.GetSelectedTranscriptionModelSize();
        var ffmpegMissing = !status.FFmpegAvailable;
        var selectedModelInstalled = status.WhisperModelAvailable &&
                                     status.InstalledModelSize == selectedSize;
        var canDownload = ffmpegMissing || !selectedModelInstalled;

        SettingsPageView.SetTranscriptionDownloadAvailability(canDownload);
    }

    private void TranscriptionModelSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateTranscriptionDownloadAvailability();
    }

    private async void TranscriptionDownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_transcriptionService is null)
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Status.Transcription.ServiceUnavailable");
            return;
        }

        var modelSize = SettingsPageView?.GetSelectedTranscriptionModelSize()
                        ?? _settings.Transcription?.ModelSize
                        ?? WhisperModelSize.Small;
        var status = _transcriptionService.GetDependencyStatus();

        var ffmpegMissing = !status.FFmpegAvailable;
        var selectedModelAlreadyInstalled = status.WhisperModelAvailable
            && status.InstalledModelSize == modelSize;

        if (!ffmpegMissing && selectedModelAlreadyInstalled)
        {
            StatusTextBlock.Text = LocalizationHelper.Format("Status.Transcription.ModelAlreadyInstalled", modelSize);
            UpdateTranscriptionDownloadAvailability(status);
            return;
        }

        SettingsPageView?.SetTranscriptionDownloadState(true);

        try
        {
            var progress = new Progress<DependencyDownloadProgress>(p =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    SettingsPageView?.UpdateTranscriptionDownloadProgress(p.Percent);

                    var defaultType = p.Type == DependencyType.FFmpeg
                        ? LocalizationHelper.Get("Transcription.Dependency.Type.FFmpeg")
                        : LocalizationHelper.Get("Transcription.Dependency.Type.Whisper");
                    var message = p.Message ?? LocalizationHelper.Format("Transcription.Dependency.Progress.Percent", defaultType, p.Percent);
                    if (p.TotalBytes > 0 && p.BytesDownloaded > 0)
                    {
                        var downloadedMB = p.BytesDownloaded / (1024.0 * 1024.0);
                        var totalMB = p.TotalBytes / (1024.0 * 1024.0);
                        message = LocalizationHelper.Format("Transcription.Dependency.Progress.Bytes", defaultType, downloadedMB, totalMB);
                    }

                    StatusTextBlock.Text = message;
                });
            });

            var requiresModelDownload = !selectedModelAlreadyInstalled;

            var success = await _transcriptionService.EnsureDependenciesAsync(
                modelSize,
                progress,
                CancellationToken.None);

            if (success)
            {
                if (requiresModelDownload)
                {
                    _transcriptionService.RemoveOtherModels(modelSize);
                }

                StatusTextBlock.Text = LocalizationHelper.Get("Status.Transcription.DownloadSuccess");
                _logger.Info(LocalizationHelper.Get("Log.Transcription.DependenciesLoaded"), TranscriptionLogSource);
            }
            else
            {
                StatusTextBlock.Text = LocalizationHelper.Get("Status.Transcription.DownloadFailed");
                _logger.Warning(LocalizationHelper.Get("Log.Transcription.DownloadFailed"), TranscriptionLogSource);
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = LocalizationHelper.Format("Status.Transcription.DownloadError", ex.Message);
            _logger.Error(LocalizationHelper.Format("Log.Transcription.DownloadError", ex.Message), TranscriptionLogSource, ex);
        }
        finally
        {
            SettingsPageView?.SetTranscriptionDownloadState(false);
            UpdateTranscriptionStatusDisplay();
        }
    }
    /*
        private void TranscriptionSettingsSaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveTranscriptionSettings();
            StatusTextBlock.Text = LocalizationHelper.Get("Status.Transcription.SettingsSaved");
        }
    */
    #endregion
}
