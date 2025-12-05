using System.IO;
using System.Windows;
using DCM.Core;
using DCM.Core.Configuration;
using DCM.Transcription;

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

        var videoPath = VideoPathTextBox.Text;

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
            TranscriptTextBox.Text = result.Text;
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

        TranscriptionProgressBar.Visibility = Visibility.Visible;
        TranscriptionProgressBar.IsIndeterminate = true;
        TranscriptionProgressBar.Value = 0;
        TranscriptionPhaseTextBlock.Visibility = Visibility.Visible;
        UpdateTranscriptionPhaseText("Initialisiere...");
    }

    private void HideTranscriptionProgress()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(HideTranscriptionProgress);
            return;
        }

        TranscriptionProgressBar.Visibility = Visibility.Collapsed;
        TranscriptionProgressBar.IsIndeterminate = false;
        TranscriptionProgressBar.Value = 0;

        // Phase-Text nach kurzer Zeit ausblenden
        Task.Delay(3000).ContinueWith(_ =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (!_isTranscribing)
                {
                    TranscriptionPhaseTextBlock.Visibility = Visibility.Collapsed;
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

        TranscriptionPhaseTextBlock.Text = text;
        TranscriptionPhaseTextBlock.Visibility = Visibility.Visible;
    }

    private void ReportDependencyProgress(DependencyDownloadProgress progress)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ReportDependencyProgress(progress));
            return;
        }

        TranscriptionProgressBar.IsIndeterminate = false;
        TranscriptionProgressBar.Value = progress.Percent;

        var typeText = progress.Type == DependencyType.FFmpeg ? "FFmpeg" : "Whisper-Modell";
        var message = $"{typeText}: {progress.Percent:F0}%";

        if (progress.TotalBytes > 0 && progress.BytesDownloaded > 0)
        {
            var downloadedMB = progress.BytesDownloaded / (1024.0 * 1024.0);
            var totalMB = progress.TotalBytes / (1024.0 * 1024.0);
            message = $"{typeText}: {downloadedMB:F1} / {totalMB:F1} MB";
        }

        UpdateTranscriptionPhaseText(message);
        StatusTextBlock.Text = progress.Message ?? message;
    }

    private void ReportTranscriptionProgress(TranscriptionProgress progress)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ReportTranscriptionProgress(progress));
            return;
        }

        TranscriptionProgressBar.IsIndeterminate = progress.Phase == TranscriptionPhase.Initializing;
        TranscriptionProgressBar.Value = progress.Percent;

        var phaseText = progress.Phase switch
        {
            TranscriptionPhase.Initializing => "Initialisiere...",
            TranscriptionPhase.ExtractingAudio => $"Audio extrahieren... {progress.Percent:F0}%",
            TranscriptionPhase.Transcribing => FormatTranscribingProgress(progress),
            TranscriptionPhase.Completed => "Abgeschlossen",
            TranscriptionPhase.Failed => progress.Message ?? "Fehlgeschlagen",
            _ => progress.Message ?? progress.Phase.ToString()
        };

        UpdateTranscriptionPhaseText(phaseText);
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

    private void UpdateTranscriptionButtonState()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(UpdateTranscriptionButtonState);
            return;
        }

        var hasVideo = !string.IsNullOrWhiteSpace(VideoPathTextBox.Text);

        if (_isTranscribing)
        {
            TranscribeButton.Content = "⏹ Abbrechen";
            TranscribeButton.IsEnabled = true;
        }
        else
        {
            TranscribeButton.Content = "🎤 Transkribieren";
            TranscribeButton.IsEnabled = hasVideo;
        }
    }

    #endregion

    #region Auto-Transcription

    private async Task TryAutoTranscribeAsync(string videoFilePath)
    {
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

        // Nur wenn das Transkript-Feld leer ist
        if (!string.IsNullOrWhiteSpace(TranscriptTextBox.Text))
        {
            return;
        }

        _logger.Debug("Auto-Transkription gestartet", "Transcription");
        await StartTranscriptionAsync(videoFilePath);
    }

    #endregion

    #region Transcription Settings UI

    private void ApplyTranscriptionSettingsToUi()
    {
        var settings = _settings.Transcription ?? new TranscriptionSettings();

        TranscriptionAutoCheckBox.IsChecked = settings.AutoTranscribeOnVideoSelect;

        // Modellgröße
        foreach (var item in TranscriptionModelSizeComboBox.Items)
        {
            if (item is System.Windows.Controls.ComboBoxItem comboItem &&
                comboItem.Tag is WhisperModelSize size &&
                size == settings.ModelSize)
            {
                TranscriptionModelSizeComboBox.SelectedItem = comboItem;
                break;
            }
        }

        // Sprache
        var languageTag = settings.Language ?? "auto";
        foreach (var item in TranscriptionLanguageComboBox.Items)
        {
            if (item is System.Windows.Controls.ComboBoxItem comboItem &&
                comboItem.Tag is string lang &&
                lang == languageTag)
            {
                TranscriptionLanguageComboBox.SelectedItem = comboItem;
                break;
            }
        }

        UpdateTranscriptionStatusDisplay();
    }

    private void SaveTranscriptionSettings()
    {
        _settings.Transcription ??= new TranscriptionSettings();
        var settings = _settings.Transcription;

        settings.AutoTranscribeOnVideoSelect = TranscriptionAutoCheckBox.IsChecked == true;

        if (TranscriptionModelSizeComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem modelItem &&
            modelItem.Tag is WhisperModelSize modelSize)
        {
            settings.ModelSize = modelSize;
        }

        if (TranscriptionLanguageComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem langItem &&
            langItem.Tag is string lang)
        {
            settings.Language = lang == "auto" ? null : lang;
        }

        SaveSettings();
    }

    private void UpdateTranscriptionStatusDisplay()
    {
        if (_transcriptionService is null)
        {
            TranscriptionStatusTextBlock.Text = "Service nicht verfügbar";
            TranscriptionStatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
            return;
        }

        var status = _transcriptionService.GetDependencyStatus();

        if (status.AllAvailable)
        {
            var modelName = status.InstalledModelSize?.ToString() ?? "Unbekannt";
            TranscriptionStatusTextBlock.Text = $"✓ Bereit (Modell: {modelName})";
            TranscriptionStatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
        }
        else
        {
            var missing = new List<string>();
            if (!status.FFmpegAvailable) missing.Add("FFmpeg");
            if (!status.WhisperModelAvailable) missing.Add("Whisper-Modell");

            TranscriptionStatusTextBlock.Text = $"✗ Fehlt: {string.Join(", ", missing)}";
            TranscriptionStatusTextBlock.Foreground = System.Windows.Media.Brushes.Orange;
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

        TranscriptionDownloadButton.IsEnabled = false;
        TranscriptionDownloadProgressBar.Visibility = Visibility.Visible;
        TranscriptionDownloadProgressBar.IsIndeterminate = true;

        try
        {
            var progress = new Progress<DependencyDownloadProgress>(p =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    TranscriptionDownloadProgressBar.IsIndeterminate = false;
                    TranscriptionDownloadProgressBar.Value = p.Percent;

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
            TranscriptionDownloadButton.IsEnabled = true;
            TranscriptionDownloadProgressBar.Visibility = Visibility.Collapsed;
            UpdateTranscriptionStatusDisplay();
        }
    }

    private void TranscriptionSettingsSaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveTranscriptionSettings();
        StatusTextBlock.Text = "Transkriptions-Einstellungen gespeichert.";
    }

    #endregion
}