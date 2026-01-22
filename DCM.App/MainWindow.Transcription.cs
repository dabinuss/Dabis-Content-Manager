using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using DCM.Core;
using DCM.Core.Configuration;
using DCM.Transcription;
using System.Threading.Tasks;
using System.Linq;
using System.Windows.Controls;
using DCM.App.Models;
using System.Globalization;

namespace DCM.App;

public partial class MainWindow
{
    private const string TranscriptionLogSource = "Transcription";
    private ITranscriptionService? _transcriptionService;
    private CancellationTokenSource? _transcriptionCts;
    private bool _isTranscribing;
    private UploadDraft? _activeTranscriptionDraft;

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
            await StartNextQueuedAutoTranscriptionCoreAsync();
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

        var draft = _activeDraft;
        if (draft is null)
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Status.Transcription.SelectVideo");
            return;
        }

        var videoPath = draft.VideoPath;
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Status.Transcription.SelectVideo");
            return;
        }

        await StartTranscriptionAsync(draft);
    }

    private async Task StartTranscriptionAsync(UploadDraft draft)
    {
        if (_transcriptionService is null)
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Status.Transcription.ServiceUnavailable");
            return;
        }

        if (!_uploadDrafts.Contains(draft))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(draft.VideoPath) || !File.Exists(draft.VideoPath))
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Status.Transcription.SelectVideo");
            return;
        }

        RemoveDraftFromTranscriptionQueue(draft.Id, persist: false);

        _isTranscribing = true;
        _transcriptionCts = new CancellationTokenSource();
        var cancellationToken = _transcriptionCts.Token;
        _activeTranscriptionDraft = draft;
        var initializingText = LocalizationHelper.Get("Transcription.Phase.Initializing");
        UpdateTranscriptionStatus(
            draft,
            UploadDraftTranscriptionState.Running,
            initializingText,
            isProgressIndeterminate: true,
            progressPercent: 0,
            updateStatusText: false);
        ScheduleDraftPersistence();

        UpdateTranscriptionButtonState();
        ShowTranscriptionProgress();

        _logger.Info(
            LocalizationHelper.Format("Log.Transcription.Started", Path.GetFileName(draft.VideoPath)),
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
            var language = ResolveTranscriptionLanguage(draft);
            var transcriptionProgress = new Progress<TranscriptionProgress>(ReportTranscriptionProgress);

                var result = await _transcriptionService.TranscribeAsync(
                    draft.VideoPath,
                    language,
                    transcriptionProgress,
                    cancellationToken);

            // Ergebnis verarbeiten
            OnTranscriptionCompleted(draft, result);
        }
        catch (OperationCanceledException)
        {
            var canceledStatus = LocalizationHelper.Get("Status.Transcription.Canceled");
            UpdateTranscriptionStatus(draft, UploadDraftTranscriptionState.Failed, canceledStatus);
            UpdateTranscriptionPhaseText(LocalizationHelper.Get("Transcription.Phase.Canceled"));
            _logger.Debug(LocalizationHelper.Get("Log.Transcription.Canceled"), TranscriptionLogSource);
        }
        catch (Exception ex)
        {
            var errorStatus = LocalizationHelper.Format("Status.Transcription.Error", ex.Message);
            UpdateTranscriptionStatus(draft, UploadDraftTranscriptionState.Failed, errorStatus);
            UpdateTranscriptionPhaseText(LocalizationHelper.Format("Transcription.Phase.Error", ex.Message));
            _logger.Error(LocalizationHelper.Format("Log.Transcription.Failed", ex.Message), TranscriptionLogSource, ex);
        }
        finally
        {
            _isTranscribing = false;
            _transcriptionCts?.Dispose();
            _transcriptionCts = null;
            if (draft.TranscriptionState == UploadDraftTranscriptionState.Running)
            {
                var failedPhaseText = LocalizationHelper.Get("Transcription.Phase.Failed");
                UpdateTranscriptionStatus(
                    draft,
                    UploadDraftTranscriptionState.Failed,
                    failedPhaseText,
                    updateStatusText: false);
            }

            HideTranscriptionProgress();
            UpdateTranscriptionButtonState();
            UpdateLogLinkIndicator();
            if (ReferenceEquals(_activeTranscriptionDraft, draft))
            {
                _activeTranscriptionDraft = null;
            }

            _ = StartNextQueuedAutoTranscriptionAsync();
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

    private void OnTranscriptionCompleted(UploadDraft draft, TranscriptionResult result)
    {
        if (result.Success && !string.IsNullOrWhiteSpace(result.Text))
        {
            draft.Transcript = result.Text;
            if (draft == _activeDraft)
            {
                UploadView.TranscriptTextBox.Text = result.Text;
            }

            var completedStatus = LocalizationHelper.Format(
                "Status.Transcription.Completed",
                result.Duration.TotalSeconds);
            UpdateTranscriptionStatus(
                draft,
                UploadDraftTranscriptionState.Completed,
                completedStatus,
                isProgressIndeterminate: false,
                progressPercent: 100);
            UpdateTranscriptionPhaseText(LocalizationHelper.Format("Transcription.Phase.CompletedIn", result.Duration.TotalSeconds));
            _logger.Info(
                LocalizationHelper.Format("Log.Transcription.Success", result.Text.Length, result.Duration.TotalSeconds),
                TranscriptionLogSource);
            ScheduleDraftPersistence();
            TryAutoRemoveDraft(draft);
        }
        else
        {
            var errorText = result.ErrorMessage ?? string.Empty;
            var failureStatus = LocalizationHelper.Format(
                "Status.Transcription.ResultFailed",
                errorText);
            UpdateTranscriptionStatus(
                draft,
                UploadDraftTranscriptionState.Failed,
                failureStatus,
                isProgressIndeterminate: false,
                progressPercent: 0);
            UpdateTranscriptionPhaseText(result.ErrorMessage ?? LocalizationHelper.Get("Transcription.Phase.Failed"));
            _logger.Warning(
                LocalizationHelper.Format("Log.Transcription.Failed", result.ErrorMessage ?? string.Empty),
                TranscriptionLogSource);
            ScheduleDraftPersistence();
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

        if (_activeTranscriptionDraft is not null)
        {
            _activeTranscriptionDraft.IsTranscriptionProgressIndeterminate = false;
            _activeTranscriptionDraft.TranscriptionProgress = Math.Clamp(progress.Percent, 0, 100);
            _activeTranscriptionDraft.TranscriptionStatus = message;
        }
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

        if (_activeTranscriptionDraft is not null)
        {
            var percent = Math.Clamp(progress.Percent, 0, 100);
            var isIndeterminate = progress.Phase == TranscriptionPhase.Initializing;
            _activeTranscriptionDraft.IsTranscriptionProgressIndeterminate = isIndeterminate;
            _activeTranscriptionDraft.TranscriptionProgress = percent;
            _activeTranscriptionDraft.TranscriptionStatus = phaseText;
        }
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
        OnUiThread(() => button.Tag = isActive ? "active" : "default");
    }

    // Optional: Mit Enable/Disable
    private void SetButtonState(Button button, bool isActive, bool isEnabled)
    {
        OnUiThread(() =>
        {
            button.Tag = isActive ? "active" : "default";
            button.IsEnabled = isEnabled;
        });
    }

    private void UpdateTranscriptionButtonState()
    {
        var hasVideo = _activeDraft?.HasVideo == true;
        SetButtonState(UploadView.TranscribeButton, _isTranscribing, _isTranscribing || hasVideo);
    }

    #endregion

    #region Auto-Transcription

    private async Task TryAutoTranscribeAsync(UploadDraft draft)
    {
        // Kleine Verzögerung um UI-Thread Zeit zu geben
        await Task.Delay(100);

        if (_transcriptionService is null)
        {
            return;
        }

        if (!_uploadDrafts.Contains(draft))
        {
            return;
        }

        if (_settings.Transcription?.AutoTranscribeOnVideoSelect != true)
        {
            return;
        }

        if (!draft.HasVideo)
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
            await Dispatcher.InvokeAsync(async () => await TryAutoTranscribeAsync(draft));
            return;
        }

        if (_isTranscribing)
        {
            QueueDraftForTranscription(draft);
            return;
        }

        // Nur wenn das Transkript-Feld leer ist
        var transcriptText = draft == _activeDraft
            ? UploadView.TranscriptTextBox.Text
            : draft.Transcript;
        if (!string.IsNullOrWhiteSpace(transcriptText))
        {
            return;
        }

        _logger.Debug(LocalizationHelper.Get("Log.Transcription.AutoStarted"), TranscriptionLogSource);
        await StartTranscriptionAsync(draft);
    }

    private void QueueDraftForTranscription(UploadDraft draft)
    {
        if (!draft.HasVideo)
        {
            return;
        }

        if (!_transcriptionQueue.Contains(draft.Id))
        {
            _transcriptionQueue.Add(draft.Id);
        }

        MarkDraftQueued(draft);
        ScheduleDraftPersistence();
    }

    private Task StartNextQueuedAutoTranscriptionAsync()
    {
        if (!Dispatcher.CheckAccess())
        {
            return Dispatcher.InvokeAsync(StartNextQueuedAutoTranscriptionCoreAsync).Task;
        }

        return StartNextQueuedAutoTranscriptionCoreAsync();
    }

    private async Task StartNextQueuedAutoTranscriptionCoreAsync()
    {
        if (_settings.Transcription?.AutoTranscribeOnVideoSelect != true)
        {
            return;
        }

        if (_isTranscribing)
        {
            return;
        }

        UploadDraft? nextDraft = null;
        var queueChanged = false;

        while (_transcriptionQueue.Count > 0 && nextDraft is null)
        {
            var nextId = _transcriptionQueue[0];
            RemoveDraftFromTranscriptionQueue(nextId, persist: false);
            queueChanged = true;
            nextDraft = _uploadDrafts.FirstOrDefault(d =>
                d.Id == nextId &&
                d.HasVideo &&
                string.IsNullOrWhiteSpace(d.Transcript));
        }

        if (nextDraft is null)
        {
            var candidate = _uploadDrafts
                .FirstOrDefault(d =>
                    d.HasVideo &&
                    string.IsNullOrWhiteSpace(d.Transcript) &&
                    (d.TranscriptionState == UploadDraftTranscriptionState.Pending ||
                     d.TranscriptionState == UploadDraftTranscriptionState.None));

            if (candidate is null)
            {
                if (queueChanged)
                {
                    ScheduleDraftPersistence();
                }

                return;
            }

            var wasQueued = _transcriptionQueue.Contains(candidate.Id);
            RemoveDraftFromTranscriptionQueue(candidate.Id, persist: false);
            if (wasQueued)
            {
                queueChanged = true;
            }

            nextDraft = candidate;
        }

        ScheduleDraftPersistence();

        await StartTranscriptionAsync(nextDraft);
    }

    private void MoveDraftToFrontOfTranscriptionQueue(UploadDraft draft)
    {
        if (!draft.HasVideo)
        {
            return;
        }

        RemoveDraftFromTranscriptionQueue(draft.Id, persist: false);
        _transcriptionQueue.Insert(0, draft.Id);
        MarkDraftQueued(draft);
        ScheduleDraftPersistence();
    }

    private void MarkDraftQueued(UploadDraft draft)
    {
        var queuedStatus = LocalizationHelper.Get("Status.Transcription.Queued");
        UpdateTranscriptionStatus(
            draft,
            UploadDraftTranscriptionState.Pending,
            queuedStatus,
            isProgressIndeterminate: true,
            progressPercent: 0,
            updateStatusText: false);
    }

    private void UpdateTranscriptionStatus(
        UploadDraft draft,
        UploadDraftTranscriptionState state,
        string statusText,
        bool? isProgressIndeterminate = null,
        double? progressPercent = null,
        bool updateStatusText = true)
    {
        draft.TranscriptionState = state;
        draft.TranscriptionStatus = statusText;

        if (isProgressIndeterminate.HasValue)
        {
            draft.IsTranscriptionProgressIndeterminate = isProgressIndeterminate.Value;
        }

        if (progressPercent.HasValue)
        {
            draft.TranscriptionProgress = progressPercent.Value;
        }

        if (updateStatusText)
        {
            StatusTextBlock.Text = statusText;
        }
    }

    private void RemoveDraftFromTranscriptionQueue(Guid draftId, bool persist = true)
    {
        var removed = false;

        while (_transcriptionQueue.Remove(draftId))
        {
            removed = true;
        }

        if (removed && persist)
        {
            ScheduleDraftPersistence();
        }
    }

    #endregion

    #region Transcription Settings UI

    private void SaveTranscriptionSettings()
    {
        _settings.Transcription ??= new TranscriptionSettings();
        GeneralSettingsPageView?.UpdateTranscriptionSettings(_settings.Transcription);
        ScheduleSettingsSave();
    }

    private void UpdateTranscriptionStatusDisplay()
    {
        if (_transcriptionService is null)
        {
            GeneralSettingsPageView?.SetTranscriptionStatus(LocalizationHelper.Get("Transcription.Status.ServiceUnavailable"), System.Windows.Media.Brushes.Red);
            GeneralSettingsPageView?.SetTranscriptionDownloadAvailability(false);
            return;
        }

        var status = _transcriptionService.GetDependencyStatus();

        if (status.AllAvailable)
        {
            var modelName = status.InstalledModelSize?.ToString() ?? LocalizationHelper.Get("Common.Unknown");
            GeneralSettingsPageView?.SetTranscriptionStatus(LocalizationHelper.Format("Transcription.Status.Ready", modelName), System.Windows.Media.Brushes.Green);
        }
        else
        {
            var missing = new List<string>();
            if (!status.FFmpegAvailable) missing.Add(LocalizationHelper.Get("Transcription.Dependency.Type.FFmpeg"));
            if (!status.WhisperModelAvailable) missing.Add(LocalizationHelper.Get("Transcription.Dependency.Type.Whisper"));

            GeneralSettingsPageView?.SetTranscriptionStatus(LocalizationHelper.Format("Transcription.Status.Missing", string.Join(", ", missing)), System.Windows.Media.Brushes.Orange);
        }

        UpdateTranscriptionDownloadAvailability(status);
    }

    private void UpdateTranscriptionDownloadAvailability()
    {
        if (GeneralSettingsPageView is null)
        {
            return;
        }

        var status = _transcriptionService?.GetDependencyStatus() ?? DependencyStatus.None;
        UpdateTranscriptionDownloadAvailability(status);
    }

    private void UpdateTranscriptionDownloadAvailability(DependencyStatus status)
    {
        if (GeneralSettingsPageView is null)
        {
            return;
        }

        var selectedSize = GeneralSettingsPageView.GetSelectedTranscriptionModelSize();
        var ffmpegMissing = !status.FFmpegAvailable;
        var selectedModelInstalled = status.WhisperModelAvailable &&
                                     status.InstalledModelSize == selectedSize;
        var canDownload = ffmpegMissing || !selectedModelInstalled;

        GeneralSettingsPageView.SetTranscriptionDownloadAvailability(canDownload);
    }

    private string? ResolveTranscriptionLanguage(UploadDraft draft)
    {
        var raw = draft.Language;
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = _settings.Language;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = LocalizationManager.Instance.CurrentLanguage;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = _settings.Transcription?.Language;
        }

        return NormalizeWhisperLanguage(raw);
    }

    private static string? NormalizeWhisperLanguage(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.Trim();
        if (string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var lower = value.ToLowerInvariant();
        if (lower.StartsWith("de") || lower.Contains("deutsch") || lower.Contains("german"))
        {
            return "de";
        }

        if (lower.StartsWith("en") || lower.Contains("english"))
        {
            return "en";
        }

        try
        {
            var culture = new CultureInfo(value);
            return culture.TwoLetterISOLanguageName;
        }
        catch
        {
            return value;
        }
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

        var modelSize = GeneralSettingsPageView?.GetSelectedTranscriptionModelSize()
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

        GeneralSettingsPageView?.SetTranscriptionDownloadState(true);

        try
        {
            var progress = new Progress<DependencyDownloadProgress>(p =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    GeneralSettingsPageView?.UpdateTranscriptionDownloadProgress(p.Percent);

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
            GeneralSettingsPageView?.SetTranscriptionDownloadState(false);
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
