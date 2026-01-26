using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using DCM.Core;
using DCM.Core.Configuration;
using DCM.Transcription;
using System.Threading.Tasks;
using System.Linq;
using System.Windows.Controls;
using DCM.App.Models;
using System.Globalization;
using DCM.App.Infrastructure;

namespace DCM.App;

public partial class MainWindow
{
    private const string TranscriptionLogSource = "Transcription";
    private const int MaxParallelTranscriptions = 2;
    private readonly SemaphoreSlim _transcriptionSlots = new(MaxParallelTranscriptions, MaxParallelTranscriptions);
    private readonly SemaphoreSlim _dependencyEnsureLock = new(1, 1);
    private readonly Dictionary<Guid, TranscriptionJob> _activeTranscriptions = new();
    private readonly Dictionary<Guid, TaskCompletionSource<bool>> _transcriptionCompletionSources = new();
    private readonly Stack<ITranscriptionService> _transcriptionServicePool = new();
    private ITranscriptionService? _transcriptionService;
    private bool _isTranscribeAllActive;

    private sealed class TranscriptionJob
    {
        public TranscriptionJob(UploadDraft draft, CancellationTokenSource cancellationTokenSource)
        {
            Draft = draft ?? throw new ArgumentNullException(nameof(draft));
            CancellationTokenSource = cancellationTokenSource ?? throw new ArgumentNullException(nameof(cancellationTokenSource));
        }

        public UploadDraft Draft { get; }
        public CancellationTokenSource CancellationTokenSource { get; }
    }

    #region Transcription Initialization

    private async Task InitializeTranscriptionServiceAsync()
    {
        try
        {
            var service = await Task.Run(() => new TranscriptionService()).ConfigureAwait(false);
            _transcriptionService = service;
            _logger.Debug(LocalizationHelper.Get("Log.Transcription.ServiceInitialized"), TranscriptionLogSource);
            _ui.Run(() =>
            {
                UpdateTranscriptionButtonState();
                UpdateTranscriptionStatusDisplay();
            }, UiUpdatePolicy.StatusPriority);
            await _ui.RunAsync(
                () => StartNextQueuedAutoTranscriptionCoreAsync(ignoreAutoSetting: false),
                UiUpdatePolicy.StatusPriority);
        }
        catch (Exception ex)
        {
            _logger.Error(LocalizationHelper.Format("Log.Transcription.ServiceInitFailed", ex.Message), TranscriptionLogSource, ex);
            _transcriptionService = null;
            _ui.Run(UpdateTranscriptionStatusDisplay, UiUpdatePolicy.StatusPriority);
        }
    }

    private void DisposeTranscriptionService()
    {
        CancelAllTranscriptions();

        // Temporäre Dateien aufräumen
        CleanupTranscriptionTempFolder();

        var servicesToDispose = new List<ITranscriptionService>();
        lock (_transcriptionServicePool)
        {
            servicesToDispose.AddRange(_transcriptionServicePool);
            _transcriptionServicePool.Clear();
        }

        if (_transcriptionService is not null && !servicesToDispose.Contains(_transcriptionService))
        {
            servicesToDispose.Add(_transcriptionService);
        }

        foreach (var service in servicesToDispose.OfType<IDisposable>())
        {
            try
            {
                service.Dispose();
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
        var draft = _activeDraft;
        if (draft is null)
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Status.Transcription.SelectVideo");
            return;
        }

        if (IsDraftTranscribing(draft))
        {
            CancelTranscription(draft);
            return;
        }

        if (IsDraftQueued(draft))
        {
            MoveDraftToFrontOfTranscriptionQueue(draft);
        }

        var videoPath = draft.VideoPath;
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Status.Transcription.SelectVideo");
            return;
        }

        await StartTranscriptionAsync(draft).ConfigureAwait(false);
    }

    private async Task StartTranscriptionAsync(UploadDraft draft, bool allowQueue = true)
    {
        if (_transcriptionService is null)
        {
            _ui.Run(() => StatusTextBlock.Text = LocalizationHelper.Get("Status.Transcription.ServiceUnavailable"));
            return;
        }

        var draftAvailable = await _ui.RunAsync(() => _uploadDrafts.Contains(draft)).ConfigureAwait(false);
        if (!draftAvailable)
        {
            return;
        }

        var alreadyRunning = await _ui.RunAsync(() => IsDraftTranscribing(draft)).ConfigureAwait(false);
        if (alreadyRunning)
        {
            return;
        }

        if (allowQueue)
        {
            if (!await _transcriptionSlots.WaitAsync(0).ConfigureAwait(false))
            {
                await _ui.RunAsync(() =>
                {
                    QueueDraftForTranscription(draft);
                    UpdateTranscriptionButtonState();
                    UpdateTranscriptionProgressUiForActiveDraft();
                }).ConfigureAwait(false);
                return;
            }
        }
        else
        {
            await _transcriptionSlots.WaitAsync().ConfigureAwait(false);
        }

        var becameRunning = await _ui.RunAsync(() => IsDraftTranscribing(draft)).ConfigureAwait(false);
        if (becameRunning)
        {
            _transcriptionSlots.Release();
            if (!allowQueue)
            {
                await WaitForTranscriptionCompletionAsync(draft).ConfigureAwait(false);
            }
            return;
        }

        var transcriptionCts = new CancellationTokenSource();
        var cancellationToken = transcriptionCts.Token;
        var job = new TranscriptionJob(draft, transcriptionCts);

        var videoPath = await _ui.RunAsync(() => draft.VideoPath).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
        {
            await _ui.RunAsync(
                () => StatusTextBlock.Text = LocalizationHelper.Get("Status.Transcription.SelectVideo"))
                .ConfigureAwait(false);
            transcriptionCts.Dispose();
            _transcriptionSlots.Release();
            return;
        }

        await _ui.RunAsync(() =>
        {
            RemoveDraftFromTranscriptionQueue(draft.Id, persist: false);

            _activeTranscriptions[draft.Id] = job;
            _transcriptionCompletionSources[draft.Id] =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var initializingText = LocalizationHelper.Get("Transcription.Phase.Initializing");
            UpdateTranscriptionStatus(
                draft,
                UploadDraftTranscriptionState.Running,
                initializingText,
                isProgressIndeterminate: true,
                progressPercent: 0,
                updateStatusText: ReferenceEquals(draft, _activeDraft));
            ScheduleDraftPersistence();

            UpdateTranscriptionButtonState();
            UpdateTranscriptionProgressUiForActiveDraft();
        }).ConfigureAwait(false);

        _logger.Info(
            LocalizationHelper.Format("Log.Transcription.Started", Path.GetFileName(videoPath)),
            TranscriptionLogSource);

        ITranscriptionService? worker = null;
        try
        {
            // Dependencies sicherstellen
            var modelSize = _settings.Transcription?.ModelSize ?? WhisperModelSize.Small;

            var dependenciesReady = await EnsureTranscriptionDependenciesAsync(draft, modelSize, cancellationToken)
                .ConfigureAwait(false);
            if (!dependenciesReady)
            {
                await _ui.RunAsync(() =>
                {
                    UpdateTranscriptionStatus(
                        draft,
                        UploadDraftTranscriptionState.Failed,
                        LocalizationHelper.Get("Status.Transcription.DependenciesFailed"),
                        updateStatusText: ReferenceEquals(draft, _activeDraft));
                    ScheduleDraftPersistence();
                    UpdateTranscriptionProgressUiForActiveDraft();
                }).ConfigureAwait(false);
                _logger.Warning(LocalizationHelper.Get("Log.Transcription.DependenciesMissing"), TranscriptionLogSource);
                return;
            }

            worker = await AcquireTranscriptionServiceAsync().ConfigureAwait(false);
            var workerReady = await worker.EnsureDependenciesAsync(
                modelSize,
                progress: null,
                cancellationToken).ConfigureAwait(false);
            if (!workerReady)
            {
                await _ui.RunAsync(() =>
                {
                    UpdateTranscriptionStatus(
                        draft,
                        UploadDraftTranscriptionState.Failed,
                        LocalizationHelper.Get("Status.Transcription.DependenciesFailed"),
                        updateStatusText: ReferenceEquals(draft, _activeDraft));
                    ScheduleDraftPersistence();
                    UpdateTranscriptionProgressUiForActiveDraft();
                }).ConfigureAwait(false);
                _logger.Warning(LocalizationHelper.Get("Log.Transcription.DependenciesMissing"), TranscriptionLogSource);
                return;
            }
            // Transkription starten
            var language = await _ui.RunAsync(() => ResolveTranscriptionLanguage(draft)).ConfigureAwait(false);
            var transcriptionProgress = new Progress<TranscriptionProgress>(progress =>
                ReportTranscriptionProgress(draft, progress));

            var result = await worker.TranscribeAsync(
                videoPath,
                language,
                transcriptionProgress,
                cancellationToken).ConfigureAwait(false);

            // Ergebnis verarbeiten
            if (!result.Success && cancellationToken.IsCancellationRequested)
            {
                await _ui.RunAsync(() => MarkTranscriptionCancelled(draft, result.Duration))
                    .ConfigureAwait(false);
                return;
            }

            await _ui.RunAsync(() => OnTranscriptionCompleted(draft, result)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await _ui.RunAsync(() => MarkTranscriptionCancelled(draft, duration: default))
                .ConfigureAwait(false);
            _logger.Debug(LocalizationHelper.Get("Log.Transcription.Canceled"), TranscriptionLogSource);
        }
        catch (Exception ex)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                await _ui.RunAsync(() => MarkTranscriptionCancelled(draft, duration: default))
                    .ConfigureAwait(false);
                _logger.Debug(LocalizationHelper.Get("Log.Transcription.Canceled"), TranscriptionLogSource);
                return;
            }

            var errorStatus = LocalizationHelper.Format("Status.Transcription.Error", ex.Message);
            await _ui.RunAsync(() =>
            {
                UpdateTranscriptionStatus(draft, UploadDraftTranscriptionState.Failed, errorStatus);
                if (ReferenceEquals(draft, _activeDraft))
                {
                    UpdateTranscriptionPhaseText(LocalizationHelper.Format("Transcription.Phase.Error", ex.Message));
                }
            }).ConfigureAwait(false);
            _logger.Error(LocalizationHelper.Format("Log.Transcription.Failed", ex.Message), TranscriptionLogSource, ex);
        }
        finally
        {
            TaskCompletionSource<bool>? completionSource = null;
            var completed = false;
            await _ui.RunAsync(() =>
            {
                _activeTranscriptions.Remove(draft.Id);
                if (_transcriptionCompletionSources.TryGetValue(draft.Id, out var source))
                {
                    _transcriptionCompletionSources.Remove(draft.Id);
                    completionSource = source;
                }

                completed = draft.TranscriptionState == UploadDraftTranscriptionState.Completed;
                if (draft.TranscriptionState == UploadDraftTranscriptionState.Running)
                {
                    var failedPhaseText = LocalizationHelper.Get("Transcription.Phase.Failed");
                    UpdateTranscriptionStatus(
                        draft,
                        UploadDraftTranscriptionState.Failed,
                        failedPhaseText,
                        updateStatusText: false);
                }

                UpdateTranscriptionButtonState();
                UpdateLogLinkIndicator();
                UpdateTranscriptionProgressUiForActiveDraft();
                UpdateTranscribeAllState();
            }).ConfigureAwait(false);

            transcriptionCts.Dispose();
            if (worker is not null)
            {
                ReleaseTranscriptionService(worker);
            }
            _transcriptionSlots.Release();
            completionSource?.TrySetResult(completed);
            _ = StartNextQueuedAutoTranscriptionAsync(ignoreAutoSetting: _isTranscribeAllActive);
        }
    }

    private async Task<bool> EnsureTranscriptionDependenciesAsync(
        UploadDraft draft,
        WhisperModelSize modelSize,
        CancellationToken cancellationToken)
    {
        if (_transcriptionService is null)
        {
            return false;
        }

        if (_transcriptionService.IsReady)
        {
            return true;
        }

        await _dependencyEnsureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_transcriptionService.IsReady)
            {
                return true;
            }

            await _ui.RunAsync(() =>
            {
                if (ReferenceEquals(draft, _activeDraft))
                {
                    UpdateTranscriptionPhaseText(LocalizationHelper.Get("Transcription.Phase.LoadingDependencies"));
                    StatusTextBlock.Text = LocalizationHelper.Get("Status.Transcription.LoadDependencies");
                }
            }).ConfigureAwait(false);

            var dependencyProgress = new Progress<DependencyDownloadProgress>(ReportDependencyProgress);
            return await _transcriptionService.EnsureDependenciesAsync(
                modelSize,
                dependencyProgress,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _dependencyEnsureLock.Release();
        }
    }

    private void CancelTranscription(UploadDraft draft)
    {
        if (!_activeTranscriptions.TryGetValue(draft.Id, out var job))
        {
            return;
        }

        try
        {
            job.CancellationTokenSource.Cancel();
            if (ReferenceEquals(draft, _activeDraft))
            {
                UpdateTranscriptionPhaseText(LocalizationHelper.Get("Transcription.Phase.Canceling"));
            }
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
            if (ReferenceEquals(draft, _activeDraft))
            {
                UpdateTranscriptionPhaseText(LocalizationHelper.Format("Transcription.Phase.CompletedIn", result.Duration.TotalSeconds));
            }
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
            if (ReferenceEquals(draft, _activeDraft))
            {
                UpdateTranscriptionPhaseText(result.ErrorMessage ?? LocalizationHelper.Get("Transcription.Phase.Failed"));
            }
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
        _ui.Run(() =>
        {
            UploadView.TranscriptionProgressBar.Visibility = Visibility.Visible;
            UploadView.TranscriptionProgressBar.IsIndeterminate = true;
            UploadView.TranscriptionProgressBar.Value = 0;
            UploadView.TranscriptionPhaseTextBlock.Visibility = Visibility.Visible;
            UpdateTranscriptionPhaseText(LocalizationHelper.Get("Transcription.Phase.Initializing"));
        }, UiUpdatePolicy.ProgressPriority);
    }

    private void HideTranscriptionProgress()
    {
        _dependencyProgressThrottle.CancelPending();
        _transcriptionProgressThrottle.CancelPending();
        _lastTranscriptionProgressPercent = -1; // Reset coalescing state
        _lastDependencyProgressPercent = -1;
        _ui.Run(() =>
        {
            UploadView.TranscriptionProgressBar.Visibility = Visibility.Collapsed;
            UploadView.TranscriptionProgressBar.IsIndeterminate = false;
            UploadView.TranscriptionProgressBar.Value = 0;

            // Phase-Text nach kurzer Zeit ausblenden
            Task.Delay(3000).ContinueWith(_ =>
            {
                _ui.Run(() =>
                {
                    var activeDraft = _activeDraft;
                    if (activeDraft is null || !IsDraftTranscribing(activeDraft))
                    {
                        UploadView.TranscriptionPhaseTextBlock.Visibility = Visibility.Collapsed;
                    }
                }, UiUpdatePolicy.ProgressPriority);
            });
        }, UiUpdatePolicy.ProgressPriority);
    }

    private void UpdateTranscriptionPhaseText(string text)
    {
        _ui.Run(() =>
        {
            if (_activeDraft is null || !IsDraftTranscribing(_activeDraft))
            {
                return;
            }

            UploadView.TranscriptionPhaseTextBlock.Text = text;
            UploadView.TranscriptionPhaseTextBlock.Visibility = Visibility.Visible;
        }, UiUpdatePolicy.ProgressPriority);
    }

    private void ReportDependencyProgress(DependencyDownloadProgress progress)
    {
        // Progress coalescing: skip updates if progress hasn't changed significantly
        if (Math.Abs(progress.Percent - _lastDependencyProgressPercent) < ProgressCoalesceThreshold)
        {
            return;
        }
        _lastDependencyProgressPercent = progress.Percent;

        _dependencyProgressThrottle.Post(() => ApplyDependencyProgress(progress));
    }

    private void ApplyDependencyProgress(DependencyDownloadProgress progress)
    {
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

        if (_activeDraft is not null && IsDraftTranscribing(_activeDraft))
        {
            _activeDraft.IsTranscriptionProgressIndeterminate = false;
            _activeDraft.TranscriptionProgress = Math.Clamp(progress.Percent, 0, 100);
            _activeDraft.TranscriptionStatus = message;
        }
    }

    private void MarkTranscriptionCancelled(UploadDraft draft, TimeSpan duration)
    {
        var canceledStatus = LocalizationHelper.Get("Status.Transcription.Canceled");
        UpdateTranscriptionStatus(
            draft,
            UploadDraftTranscriptionState.Cancelled,
            canceledStatus,
            isProgressIndeterminate: false,
            progressPercent: 0);

        if (ReferenceEquals(draft, _activeDraft))
        {
            UpdateTranscriptionPhaseText(LocalizationHelper.Get("Transcription.Phase.Canceled"));
        }

        ScheduleDraftPersistence();
    }

    private void ReportTranscriptionProgress(UploadDraft draft, TranscriptionProgress progress)
    {
        // Progress coalescing: skip updates if progress hasn't changed significantly
        // Always update for phase changes (Initializing has Percent=0, Completed has Percent=100)
        var currentPercent = progress.Percent;
        var isSignificantChange = progress.Phase == TranscriptionPhase.Initializing
            || progress.Phase == TranscriptionPhase.Completed
            || progress.Phase == TranscriptionPhase.Failed
            || Math.Abs(currentPercent - _lastTranscriptionProgressPercent) >= ProgressCoalesceThreshold;

        if (!isSignificantChange)
        {
            return;
        }
        _lastTranscriptionProgressPercent = currentPercent;

        _ui.Run(() => UpdateDraftTranscriptionProgress(draft, progress), UiUpdatePolicy.ProgressPriority);
        if (ReferenceEquals(draft, _activeDraft))
        {
            _transcriptionProgressThrottle.Post(() => ApplyTranscriptionProgress(draft, progress));
        }
    }

    private void ApplyTranscriptionProgress(UploadDraft draft, TranscriptionProgress progress)
    {
        if (!ReferenceEquals(draft, _activeDraft))
        {
            return;
        }

        UploadView.TranscriptionProgressBar.IsIndeterminate = progress.Phase == TranscriptionPhase.Initializing;
        UploadView.TranscriptionProgressBar.Value = progress.Percent;

        var phaseText = BuildTranscriptionPhaseText(progress);

        UploadView.TranscriptionPhaseTextBlock.Text = phaseText;
        UploadView.TranscriptionPhaseTextBlock.Visibility = Visibility.Visible;
        StatusTextBlock.Text = progress.Message ?? phaseText;
    }

    private void UpdateDraftTranscriptionProgress(UploadDraft draft, TranscriptionProgress progress)
    {
        var percent = Math.Clamp(progress.Percent, 0, 100);
        var isIndeterminate = progress.Phase == TranscriptionPhase.Initializing;
        var phaseText = BuildTranscriptionPhaseText(progress);

        draft.IsTranscriptionProgressIndeterminate = isIndeterminate;
        draft.TranscriptionProgress = percent;
        draft.TranscriptionStatus = phaseText;
    }

    private static string BuildTranscriptionPhaseText(TranscriptionProgress progress)
    {
        return progress.Phase switch
        {
            TranscriptionPhase.Initializing => LocalizationHelper.Get("Transcription.Phase.Initializing"),
            TranscriptionPhase.ExtractingAudio => LocalizationHelper.Format("Transcription.Phase.ExtractingAudio", progress.Percent),
            TranscriptionPhase.Transcribing => FormatTranscribingProgress(progress),
            TranscriptionPhase.Completed => LocalizationHelper.Get("Transcription.Phase.Completed"),
            TranscriptionPhase.Failed => progress.Message ?? LocalizationHelper.Get("Transcription.Phase.Failed"),
            _ => progress.Message ?? progress.Phase.ToString()
        };
    }

    private void UpdateTranscriptionProgressUiForActiveDraft()
    {
        _transcriptionProgressThrottle.CancelPending();
        if (_activeDraft is null || _activeDraft.TranscriptionState != UploadDraftTranscriptionState.Running)
        {
            HideTranscriptionProgress();
            return;
        }

        _ui.Run(() =>
        {
            UploadView.TranscriptionProgressBar.Visibility = Visibility.Visible;
            UploadView.TranscriptionProgressBar.IsIndeterminate = _activeDraft.IsTranscriptionProgressIndeterminate;
            UploadView.TranscriptionProgressBar.Value = _activeDraft.TranscriptionProgress;

            var phaseText = string.IsNullOrWhiteSpace(_activeDraft.TranscriptionStatus)
                ? LocalizationHelper.Get("Transcription.Phase.Initializing")
                : _activeDraft.TranscriptionStatus;
            UploadView.TranscriptionPhaseTextBlock.Text = phaseText;
            UploadView.TranscriptionPhaseTextBlock.Visibility = Visibility.Visible;
        }, UiUpdatePolicy.ProgressPriority);
    }

    private async Task<ITranscriptionService> AcquireTranscriptionServiceAsync()
    {
        lock (_transcriptionServicePool)
        {
            if (_transcriptionServicePool.Count > 0)
            {
                return _transcriptionServicePool.Pop();
            }
        }

        return await Task.Run(() => (ITranscriptionService)new TranscriptionService()).ConfigureAwait(false);
    }

    private void ReleaseTranscriptionService(ITranscriptionService service)
    {
        if (service is null)
        {
            return;
        }

        lock (_transcriptionServicePool)
        {
            if (!_transcriptionServicePool.Contains(service))
            {
                _transcriptionServicePool.Push(service);
            }
        }
    }

    private bool IsDraftTranscribing(UploadDraft draft)
        => _activeTranscriptions.ContainsKey(draft.Id);

    private bool IsDraftQueued(UploadDraft draft)
        => _transcriptionQueue.Contains(draft.Id);

    private int ActiveTranscriptionCount => _activeTranscriptions.Count;

    private void CancelAllTranscriptions()
    {
        foreach (var job in _activeTranscriptions.Values.ToList())
        {
            try
            {
                job.CancellationTokenSource.Cancel();
            }
            catch
            {
                // Ignorieren
            }
        }
    }

    private void CancelQueuedTranscriptions()
    {
        if (_transcriptionQueue.Count == 0)
        {
            return;
        }

        var queuedIds = _transcriptionQueue.ToList();
        _transcriptionQueue.Clear();

        // Build dictionary for O(1) lookup instead of O(n) per iteration
        var draftsById = _uploadDrafts.ToDictionary(d => d.Id);

        foreach (var draftId in queuedIds)
        {
            if (!draftsById.TryGetValue(draftId, out var draft) || IsDraftTranscribing(draft))
            {
                continue;
            }

            ResetTranscriptionState(draft);
        }

        ScheduleDraftPersistence();
        UpdateTranscriptionButtonState();
        UpdateTranscriptionProgressUiForActiveDraft();
    }

    private void UpdateTranscribeAllState()
    {
        if (!_isTranscribeAllActive)
        {
            return;
        }

        if (_activeTranscriptions.Count == 0 && _transcriptionQueue.Count == 0)
        {
            _isTranscribeAllActive = false;
            UpdateTranscribeAllActionUi();
        }
    }

    private void UpdateTranscribeAllActionUi()
    {
        if (UploadView is null)
        {
            return;
        }

        UploadView.SetTranscribeAllActionState(_isTranscribeAllActive);
    }

    private async Task<bool> WaitForTranscriptionCompletionAsync(UploadDraft draft)
    {
        var waitTask = await _ui.RunAsync(() =>
        {
            if (_transcriptionCompletionSources.TryGetValue(draft.Id, out var source))
            {
                return source.Task;
            }

            return Task.FromResult(draft.TranscriptionState == UploadDraftTranscriptionState.Completed);
        }).ConfigureAwait(false);

        return await waitTask.ConfigureAwait(false);
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
        _ui.Run(() => button.Tag = isActive ? "active" : "default", UiUpdatePolicy.ButtonPriority);
    }

    // Optional: Mit Enable/Disable
    private void SetButtonState(Button button, bool isActive, bool isEnabled)
    {
        _ui.Run(() =>
        {
            button.Tag = isActive ? "active" : "default";
            button.IsEnabled = isEnabled;
        }, UiUpdatePolicy.ButtonPriority);
    }

    private void UpdateTranscriptionButtonState()
    {
        var hasVideo = _activeDraft?.HasVideo == true;
        var isActiveDraftTranscribing = _activeDraft is not null && IsDraftTranscribing(_activeDraft);
        SetButtonState(UploadView.TranscribeButton, isActiveDraftTranscribing, isActiveDraftTranscribing || hasVideo);
    }

    #endregion

    #region Auto-Transcription

    private async Task TryAutoTranscribeAsync(UploadDraft draft)
    {
        // Kleine Verzögerung um UI-Thread Zeit zu geben
        await Task.Delay(100).ConfigureAwait(false);

        if (_transcriptionService is null)
        {
            return;
        }

        var canAutoTranscribe = await _ui.RunAsync(() =>
        {
            if (!_uploadDrafts.Contains(draft))
            {
                return false;
            }

            if (_settings.Transcription?.AutoTranscribeOnVideoSelect != true)
            {
                return false;
            }

            if (!draft.HasVideo)
            {
                return false;
            }

            return true;
        }).ConfigureAwait(false);

        if (!canAutoTranscribe)
        {
            return;
        }

        if (!_transcriptionService.IsReady)
        {
            _logger.Debug(LocalizationHelper.Get("Log.Transcription.AutoSkip"), TranscriptionLogSource);
            return;
        }

        var shouldStart = await _ui.RunAsync(() =>
        {
            if (IsDraftTranscribing(draft))
            {
                return false;
            }

            if (ActiveTranscriptionCount >= MaxParallelTranscriptions)
            {
                QueueDraftForTranscription(draft);
                return false;
            }

            // Nur wenn das Transkript-Feld leer ist
            var transcriptText = draft == _activeDraft
                ? UploadView.TranscriptTextBox.Text
                : draft.Transcript;
            if (!string.IsNullOrWhiteSpace(transcriptText))
            {
                return false;
            }

            return true;
        }).ConfigureAwait(false);

        if (!shouldStart)
        {
            return;
        }

        _logger.Debug(LocalizationHelper.Get("Log.Transcription.AutoStarted"), TranscriptionLogSource);
        await StartTranscriptionAsync(draft).ConfigureAwait(false);
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

    private Task StartNextQueuedAutoTranscriptionAsync(bool ignoreAutoSetting = false)
    {
        return _ui.RunAsync(() => StartNextQueuedAutoTranscriptionCoreAsync(ignoreAutoSetting));
    }

    private async Task StartNextQueuedAutoTranscriptionCoreAsync(bool ignoreAutoSetting)
    {
        if (!ignoreAutoSetting && _settings.Transcription?.AutoTranscribeOnVideoSelect != true)
        {
            return;
        }

        var draftsToStart = await _ui.RunAsync(() =>
        {
            var availableSlots = Math.Max(0, MaxParallelTranscriptions - ActiveTranscriptionCount);
            if (availableSlots == 0)
            {
                return new List<UploadDraft>();
            }

            var queueChanged = false;
            var toStart = new List<UploadDraft>(availableSlots);
            var visitedIds = new HashSet<Guid>();

            // Build dictionary for O(1) lookup instead of O(n) per iteration
            var draftsById = _uploadDrafts.ToDictionary(d => d.Id);

            while (_transcriptionQueue.Count > 0 && toStart.Count < availableSlots)
            {
                var nextId = _transcriptionQueue[0];
                RemoveDraftFromTranscriptionQueue(nextId, persist: false);
                queueChanged = true;
                visitedIds.Add(nextId);

                if (draftsById.TryGetValue(nextId, out var candidate) &&
                    candidate.HasVideo &&
                    string.IsNullOrWhiteSpace(candidate.Transcript) &&
                    !IsDraftTranscribing(candidate))
                {
                    toStart.Add(candidate);
                }
            }

            if (toStart.Count < availableSlots)
            {
                foreach (var candidate in _uploadDrafts)
                {
                    if (toStart.Count >= availableSlots)
                    {
                        break;
                    }

                    if (!candidate.HasVideo ||
                        !string.IsNullOrWhiteSpace(candidate.Transcript) ||
                        (candidate.TranscriptionState != UploadDraftTranscriptionState.Pending &&
                         candidate.TranscriptionState != UploadDraftTranscriptionState.None &&
                         candidate.TranscriptionState != UploadDraftTranscriptionState.Cancelled) ||
                        IsDraftTranscribing(candidate) ||
                        visitedIds.Contains(candidate.Id))
                    {
                        continue;
                    }

                    var wasQueued = _transcriptionQueue.Contains(candidate.Id);
                    RemoveDraftFromTranscriptionQueue(candidate.Id, persist: false);
                    if (wasQueued)
                    {
                        queueChanged = true;
                    }

                    toStart.Add(candidate);
                }
            }

            if (queueChanged)
            {
                ScheduleDraftPersistence();
            }

            return toStart;
        }).ConfigureAwait(false);

        if (draftsToStart.Count == 0)
        {
            return;
        }

        foreach (var draft in draftsToStart)
        {
            _ = StartTranscriptionAsync(draft).ConfigureAwait(false);
        }
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

        if (updateStatusText && ReferenceEquals(draft, _activeDraft))
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

    private void QueueDependencyDownloadProgress(DependencyDownloadProgress progress)
    {
        // Progress coalescing: skip updates if progress hasn't changed significantly
        if (Math.Abs(progress.Percent - _lastDependencyProgressPercent) < ProgressCoalesceThreshold)
        {
            return;
        }
        _lastDependencyProgressPercent = progress.Percent;

        _dependencyDownloadThrottle.Post(() => ApplyDependencyDownloadProgress(progress));
    }

    private void ApplyDependencyDownloadProgress(DependencyDownloadProgress progress)
    {
        GeneralSettingsPageView?.UpdateTranscriptionDownloadProgress(progress.Percent);

        var defaultType = progress.Type == DependencyType.FFmpeg
            ? LocalizationHelper.Get("Transcription.Dependency.Type.FFmpeg")
            : LocalizationHelper.Get("Transcription.Dependency.Type.Whisper");
        var message = progress.Message ?? LocalizationHelper.Format("Transcription.Dependency.Progress.Percent", defaultType, progress.Percent);
        if (progress.TotalBytes > 0 && progress.BytesDownloaded > 0)
        {
            var downloadedMB = progress.BytesDownloaded / (1024.0 * 1024.0);
            var totalMB = progress.TotalBytes / (1024.0 * 1024.0);
            message = LocalizationHelper.Format("Transcription.Dependency.Progress.Bytes", defaultType, downloadedMB, totalMB);
        }

        StatusTextBlock.Text = message;
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
            var progress = new Progress<DependencyDownloadProgress>(QueueDependencyDownloadProgress);

            var requiresModelDownload = !selectedModelAlreadyInstalled;

            var success = await _transcriptionService.EnsureDependenciesAsync(
                modelSize,
                progress,
                CancellationToken.None).ConfigureAwait(false);

            if (success)
            {
                if (requiresModelDownload)
                {
                    _transcriptionService.RemoveOtherModels(modelSize);
                }

                await _ui.RunAsync(
                    () => StatusTextBlock.Text = LocalizationHelper.Get("Status.Transcription.DownloadSuccess"))
                    .ConfigureAwait(false);
                _logger.Info(LocalizationHelper.Get("Log.Transcription.DependenciesLoaded"), TranscriptionLogSource);
            }
            else
            {
                await _ui.RunAsync(
                    () => StatusTextBlock.Text = LocalizationHelper.Get("Status.Transcription.DownloadFailed"))
                    .ConfigureAwait(false);
                _logger.Warning(LocalizationHelper.Get("Log.Transcription.DownloadFailed"), TranscriptionLogSource);
            }
        }
        catch (Exception ex)
        {
            await _ui.RunAsync(
                () => StatusTextBlock.Text = LocalizationHelper.Format("Status.Transcription.DownloadError", ex.Message))
                .ConfigureAwait(false);
            _logger.Error(LocalizationHelper.Format("Log.Transcription.DownloadError", ex.Message), TranscriptionLogSource, ex);
        }
        finally
        {
            _dependencyDownloadThrottle.CancelPending();
            _ui.Run(() =>
            {
                GeneralSettingsPageView?.SetTranscriptionDownloadState(false);
                UpdateTranscriptionStatusDisplay();
            }, UiUpdatePolicy.StatusPriority);
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
