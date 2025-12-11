using System.Collections.Generic;
using System.IO;
using System.Windows;
using DCM.Core;
using DCM.Core.Configuration;
using DCM.Transcription;
using System.Windows.Controls;

namespace DCM.App;

public partial class MainWindow
{
    private ITranscriptionService? _transcriptionService;
    private CancellationTokenSource? _transcriptionCts;
    private bool _isTranscribing;

    #region Transcription Initialization

    private void InitializeTranscriptionService()
    {
        try
        {
            _transcriptionService = new TranscriptionService();
            _logger.Debug("TranscriptionService initialisiert", "Transcription");
            UpdateTranscriptionButtonState();
            UpdateTranscriptionStatusDisplay();
        }
        catch (Exception ex)
        {
            _logger.Error($"TranscriptionService konnte nicht initialisiert werden: {ex.Message}", "Transcription", ex);
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
            StatusTextBlock.Text = "Bitte zuerst ein Video auswählen.";
            return;
        }

        await StartTranscriptionAsync(videoPath);
    }

    private async Task StartTranscriptionAsync(string videoFilePath)
    {
        if (_transcriptionService is null)
        {
            StatusTextBlock.Text = "Transkriptions-Service nicht verfügbar.";
            return;
        }

        _isTranscribing = true;
        _transcriptionCts = new CancellationTokenSource();
        var cancellationToken = _transcriptionCts.Token;

        UpdateTranscriptionButtonState();
        ShowTranscriptionProgress();

        _logger.Info($"Transkription gestartet: {Path.GetFileName(videoFilePath)}", "Transcription");

        try
        {
            // Dependencies sicherstellen
            var modelSize = _settings.Transcription?.ModelSize ?? WhisperModelSize.Small;

            if (!_transcriptionService.IsReady)
            {
                UpdateTranscriptionPhaseText("Lade Abhängigkeiten...");
                StatusTextBlock.Text = "Lade Transkriptions-Abhängigkeiten...";

                var dependencyProgress = new Progress<DependencyDownloadProgress>(ReportDependencyProgress);
                var dependenciesReady = await _transcriptionService.EnsureDependenciesAsync(
                    modelSize,
                    dependencyProgress,
                    cancellationToken);

                if (!dependenciesReady)
                {
                    StatusTextBlock.Text = "Transkriptions-Abhängigkeiten konnten nicht geladen werden.";
                    _logger.Warning("Transkriptions-Abhängigkeiten nicht verfügbar", "Transcription");
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
            StatusTextBlock.Text = "Transkription abgebrochen.";
            UpdateTranscriptionPhaseText("Abgebrochen");
            _logger.Debug("Transkription abgebrochen", "Transcription");
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Transkription fehlgeschlagen: {ex.Message}";
            UpdateTranscriptionPhaseText($"Fehler: {ex.Message}");
            _logger.Error($"Transkription fehlgeschlagen: {ex.Message}", "Transcription", ex);
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
            UpdateTranscriptionPhaseText("Wird abgebrochen...");
            _logger.Debug("Transkription wird abgebrochen...", "Transcription");
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
            StatusTextBlock.Text = $"Transkription abgeschlossen ({result.Duration.TotalSeconds:F1}s).";
            UpdateTranscriptionPhaseText($"Abgeschlossen in {result.Duration.TotalSeconds:F1}s");
            _logger.Info($"Transkription erfolgreich: {result.Text.Length} Zeichen in {result.Duration.TotalSeconds:F1}s", "Transcription");
        }
        else
        {
            StatusTextBlock.Text = $"Transkription fehlgeschlagen: {result.ErrorMessage}";
            UpdateTranscriptionPhaseText(result.ErrorMessage ?? "Fehlgeschlagen");
            _logger.Warning($"Transkription fehlgeschlagen: {result.ErrorMessage}", "Transcription");
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
        UpdateTranscriptionPhaseText("Initialisiere...");
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

        var typeText = progress.Type == DependencyType.FFmpeg ? "FFmpeg" : "Whisper-Modell";
        var message = $"{typeText}: {progress.Percent:F0}%";

        if (progress.TotalBytes > 0 && progress.BytesDownloaded > 0)
        {
            var downloadedMB = progress.BytesDownloaded / (1024.0 * 1024.0);
            var totalMB = progress.TotalBytes / (1024.0 * 1024.0);
            message = $"{typeText}: {downloadedMB:F1} / {totalMB:F1} MB";
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
            TranscriptionPhase.Initializing => "Initialisiere...",
            TranscriptionPhase.ExtractingAudio => $"Audio extrahieren... {progress.Percent:F0}%",
            TranscriptionPhase.Transcribing => FormatTranscribingProgress(progress),
            TranscriptionPhase.Completed => "Abgeschlossen",
            TranscriptionPhase.Failed => progress.Message ?? "Fehlgeschlagen",
            _ => progress.Message ?? progress.Phase.ToString()
        };

        UploadView.TranscriptionPhaseTextBlock.Text = phaseText;
        UploadView.TranscriptionPhaseTextBlock.Visibility = Visibility.Visible;
        StatusTextBlock.Text = progress.Message ?? phaseText;
    }

    private static string FormatTranscribingProgress(TranscriptionProgress progress)
    {
        var text = $"Transkribiere... {progress.Percent:F0}%";

        if (progress.EstimatedTimeRemaining.HasValue && progress.EstimatedTimeRemaining.Value.TotalSeconds > 0)
        {
            var remaining = progress.EstimatedTimeRemaining.Value;
            if (remaining.TotalMinutes >= 1)
            {
                text += $" (~{remaining.TotalMinutes:F0} Min. verbleibend)";
            }
            else
            {
                text += $" (~{remaining.TotalSeconds:F0}s verbleibend)";
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
            _logger.Debug("Auto-Transkription übersprungen: Dependencies nicht bereit", "Transcription");
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

        _logger.Debug("Auto-Transkription gestartet", "Transcription");
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
            SettingsPageView?.SetTranscriptionStatus("Service nicht verfügbar", System.Windows.Media.Brushes.Red);
            return;
        }

        var status = _transcriptionService.GetDependencyStatus();

        if (status.AllAvailable)
        {
            var modelName = status.InstalledModelSize?.ToString() ?? "Unbekannt";
            SettingsPageView?.SetTranscriptionStatus($"✓ Bereit (Modell: {modelName})", System.Windows.Media.Brushes.Green);
        }
        else
        {
            var missing = new List<string>();
            if (!status.FFmpegAvailable) missing.Add("FFmpeg");
            if (!status.WhisperModelAvailable) missing.Add("Whisper-Modell");

            SettingsPageView?.SetTranscriptionStatus($"✗ Fehlt: {string.Join(", ", missing)}", System.Windows.Media.Brushes.Orange);
        }
    }

    private async void TranscriptionDownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_transcriptionService is null)
        {
            StatusTextBlock.Text = "Transkriptions-Service nicht verfügbar.";
            return;
        }

        var modelSize = _settings.Transcription?.ModelSize ?? WhisperModelSize.Small;

        SettingsPageView?.SetTranscriptionDownloadState(true);

        try
        {
            var progress = new Progress<DependencyDownloadProgress>(p =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    SettingsPageView?.UpdateTranscriptionDownloadProgress(p.Percent);

                    var message = p.Message ?? $"Download: {p.Percent:F0}%";
                    if (p.TotalBytes > 0 && p.BytesDownloaded > 0)
                    {
                        var downloadedMB = p.BytesDownloaded / (1024.0 * 1024.0);
                        var totalMB = p.TotalBytes / (1024.0 * 1024.0);
                        message = $"{(p.Type == DependencyType.FFmpeg ? "FFmpeg" : "Whisper-Modell")}: {downloadedMB:F1} / {totalMB:F1} MB";
                    }

                    StatusTextBlock.Text = message;
                });
            });

            var success = await _transcriptionService.EnsureDependenciesAsync(
                modelSize,
                progress,
                CancellationToken.None);

            if (success)
            {
                StatusTextBlock.Text = "Transkriptions-Abhängigkeiten erfolgreich geladen.";
                _logger.Info("Transkriptions-Abhängigkeiten geladen", "Transcription");
            }
            else
            {
                StatusTextBlock.Text = "Download fehlgeschlagen.";
                _logger.Warning("Transkriptions-Download fehlgeschlagen", "Transcription");
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Download-Fehler: {ex.Message}";
            _logger.Error($"Transkriptions-Download-Fehler: {ex.Message}", "Transcription", ex);
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
            StatusTextBlock.Text = "Transkriptions-Einstellungen gespeichert.";
        }
    */
    #endregion
}
