using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Security.Cryptography;
using DCM.App.Models;
using DCM.App.Infrastructure;
using DCM.Core;
using DCM.Core.Configuration;
using DCM.Core.Models;
using DCM.YouTube;
using Microsoft.Win32;

namespace DCM.App;

public partial class MainWindow
{
    private const string UploadLogSource = "Upload";
    private const string PresetLogSource = "Preset";
    private const int ThumbnailDecodePixelWidth = 640;
    private const int VideoPreviewDecodePixelWidth = 640;
    private static readonly TimeSpan DraftTextDebounceDelay = TimeSpan.FromMilliseconds(350);
    private static readonly Regex DescriptionPlaceholderRegex = new(@"\{+\s*description\s*\}+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ChaptersPlaceholderRegex = new(@"\{+\s*chapters\s*\}+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly SemaphoreSlim _videoInfoSemaphore = new(2, 2);
    private readonly SemaphoreSlim _videoPreviewSemaphore = new(1, 1);
    private bool _isFastFillRunning;
    #region Draft Persistence

    private void RestoreDraftsFromSettings()
    {
        _uploadDrafts.Clear();

        if (_settings.RememberDraftsBetweenSessions != true)
        {
            _settings.SavedDrafts?.Clear();
            SetActiveDraft(null);
            UpdateUploadListVisibility();
            return;
        }

        var snapshots = _settings.SavedDrafts ?? new List<UploadDraftSnapshot>();
        if (snapshots.Count == 0)
        {
            SetActiveDraft(null);
            UpdateUploadListVisibility();
            return;
        }

        _isRestoringDrafts = true;
        var migratedThumbnails = false;
        var migratedTranscripts = false;
        var autoRemoveCompleted = _settings.AutoRemoveCompletedDrafts == true;

        try
        {
            _transcriptionQueue.Clear();

            var loadResult = _draftRepository.LoadDrafts(
                snapshots,
                autoRemoveCompleted,
                ShouldAutoRemoveDraft);
            var removedDuringRestore = loadResult.RemovedDuringRestore;
            migratedTranscripts = loadResult.MigratedTranscripts;

            foreach (var draft in loadResult.Drafts)
            {
                _uploadDrafts.Add(draft);
            }

            foreach (var draft in _uploadDrafts)
            {
                if (TryPersistDraftThumbnail(draft))
                {
                    migratedThumbnails = true;
                }
            }

            foreach (var draft in _uploadDrafts)
            {
                if (TryResolveDraftThumbnailFromVideo(draft))
                {
                    migratedThumbnails = true;
                }
            }

            foreach (var draft in _uploadDrafts)
            {
                if (draft.HasVideo && NeedsVideoInfo(draft))
                {
                    _ = LoadVideoFileInfoAsync(draft, triggerAutoTranscribe: false);
                }
            }

            foreach (var draft in _uploadDrafts)
            {
                if (draft.HasVideo && NeedsVideoPreview(draft))
                {
                    _ = EnsureVideoPreviewAsync(draft, draft.VideoPath, null);
                }
            }

            _draftTranscriptStore.CleanupOrphanedTranscripts(
                _uploadDrafts.Select(d => d.Id).ToHashSet(),
                maxAge: TimeSpan.FromDays(30));

            _ = Task.Run(CleanupDraftAssetCaches);

            var storedQueue = _settings.PendingTranscriptionQueue ?? new List<Guid>();
            foreach (var draftId in storedQueue)
            {
                var candidate = _uploadDrafts.FirstOrDefault(d => d.Id == draftId);
                if (candidate is null ||
                    !candidate.HasVideo ||
                    !string.IsNullOrWhiteSpace(candidate.Transcript))
                {
                    continue;
                }

                if (!_transcriptionQueue.Contains(draftId))
                {
                    _transcriptionQueue.Add(draftId);
                }

                if (candidate.TranscriptionState == UploadDraftTranscriptionState.None)
                {
                    MarkDraftQueued(candidate);
                }
            }

            if (_uploadDrafts.Count > 0)
            {
                SetActiveDraft(_uploadDrafts[0]);
            }
            UpdateUploadListVisibility();

            if (removedDuringRestore || migratedThumbnails || migratedTranscripts)
            {
                PersistDrafts();
            }
        }
        finally
        {
            _isRestoringDrafts = false;
        }
    }

    private void CleanupDraftAssetCaches()
    {
        try
        {
            var drafts = _uploadDrafts.ToList();
            var protectedThumbnailPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var protectedPreviewPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var protectedThumbnailIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var draft in drafts)
            {
                protectedThumbnailIds.Add(draft.Id.ToString("N"));

                if (!string.IsNullOrWhiteSpace(draft.ThumbnailPath))
                {
                    TryAddFullPath(protectedThumbnailPaths, draft.ThumbnailPath);
                }

                if (draft.HasVideo && !string.IsNullOrWhiteSpace(draft.VideoPath))
                {
                    try
                    {
                        var previewPath = BuildVideoPreviewPath(draft.VideoPath);
                        protectedPreviewPaths.Add(previewPath);
                    }
                    catch
                    {
                        // Ignore invalid paths.
                    }
                }
            }

            CleanupFolder(
                Constants.ThumbnailsFolder,
                protectedThumbnailPaths,
                protectedThumbnailIds,
                TimeSpan.FromDays(Constants.ThumbnailRetentionDays),
                Constants.ThumbnailCacheMaxBytes);

            CleanupFolder(
                Constants.VideoPreviewFolder,
                protectedPreviewPaths,
                protectedIds: null,
                TimeSpan.FromDays(Constants.VideoPreviewRetentionDays),
                Constants.VideoPreviewCacheMaxBytes);
        }
        catch
        {
            // Cleanup is best-effort.
        }
    }

    private static void TryAddFullPath(HashSet<string> target, string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            target.Add(fullPath);
        }
        catch
        {
            // Ignore invalid paths.
        }
    }

    private static void CleanupFolder(
        string folder,
        HashSet<string> protectedPaths,
        HashSet<string>? protectedIds,
        TimeSpan maxAge,
        long maxBytes)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        var threshold = DateTimeOffset.UtcNow - maxAge;
        var orphanInfos = new List<FileInfo>();

        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var fullPath = file;
            try
            {
                fullPath = Path.GetFullPath(file);
            }
            catch
            {
                // Ignore invalid paths.
            }

            if (protectedPaths.Contains(fullPath))
            {
                continue;
            }

            if (protectedIds is not null)
            {
                var name = Path.GetFileNameWithoutExtension(fullPath);
                if (!string.IsNullOrWhiteSpace(name) && protectedIds.Contains(name))
                {
                    continue;
                }
            }

            FileInfo info;
            try
            {
                info = new FileInfo(fullPath);
            }
            catch
            {
                continue;
            }

            if (!info.Exists)
            {
                continue;
            }

            if (info.LastWriteTimeUtc < threshold.UtcDateTime)
            {
                try
                {
                    info.Delete();
                }
                catch
                {
                    // Ignore delete failures.
                }
                continue;
            }

            orphanInfos.Add(info);
        }

        if (maxBytes <= 0)
        {
            return;
        }

        var totalBytes = orphanInfos.Sum(i => i.Length);
        if (totalBytes <= maxBytes)
        {
            return;
        }

        foreach (var info in orphanInfos.OrderBy(i => i.LastWriteTimeUtc))
        {
            try
            {
                info.Delete();
                totalBytes -= info.Length;
                if (totalBytes <= maxBytes)
                {
                    break;
                }
            }
            catch
            {
                // Ignore delete failures.
            }
        }
    }

    private void ScheduleDraftPersistence() => _ui.Run(_draftPersistence.Schedule, UiUpdatePolicy.LogPriority);

    private void ScheduleDraftPersistenceDebounced() => _ui.Run(_draftPersistence.ScheduleDebounced, UiUpdatePolicy.LogPriority);

    private void PersistDrafts()
    {
        if (_settings.RememberDraftsBetweenSessions != true)
        {
            _settings.SavedDrafts?.Clear();
            _settings.PendingTranscriptionQueue?.Clear();
            SaveSettings();
            return;
        }

        var snapshots = _draftRepository.CreateSnapshots(_uploadDrafts);

        _settings.SavedDrafts = snapshots;
        _settings.PendingTranscriptionQueue = _transcriptionQueue.ToList();
        SaveSettings();
    }

    private void RemoveDraft(UploadDraft draft)
    {
        if (IsDraftTranscribing(draft))
        {
            CancelTranscription(draft);
        }

        if (_isUploading && _activeUploadDraft == draft)
        {
            CancelActiveUpload();
        }

        RemoveDraftFromTranscriptionQueue(draft.Id);
        _draftTranscriptStore.DeleteTranscript(draft.Id);
        var previousIndex = _uploadDrafts.IndexOf(draft);
        var wasActive = draft == _activeDraft;
        var scrollOffset = UploadView?.GetUploadItemsVerticalOffset() ?? 0;
        UploadView?.SuppressUploadItemsBringIntoView(true);
        _uploadDrafts.Remove(draft);
        UpdateUploadListVisibility();
        ResetDraftState(draft);

        if (wasActive)
        {
            if (_uploadDrafts.Count == 0)
            {
                SetActiveDraft(null);
            }
            else
            {
                var nextIndex = Math.Min(Math.Max(previousIndex, 0), _uploadDrafts.Count - 1);
                SetActiveDraft(_uploadDrafts[nextIndex]);
            }
        }

        UploadView?.RestoreUploadItemsVerticalOffset(scrollOffset);
        UploadView?.SuppressUploadItemsBringIntoView(false);

        UpdateDraftActionStates();
        ScheduleDraftPersistence();
    }

    private void CancelActiveUpload()
    {
        if (!_isUploading || _activeUploadCts is null)
        {
            return;
        }

        try
        {
            _activeUploadCts.Cancel();
            StatusTextBlock.Text = LocalizationHelper.Get("Status.Upload.Canceled");
            _logger.Info("Upload canceled.", UploadLogSource);
        }
        catch (Exception ex)
        {
            _logger.Debug($"Cancel upload failed: {ex.Message}", UploadLogSource);
        }
        finally
        {
            UpdateUploadButtonState();
        }
    }

    private static void ResetDraftState(UploadDraft draft)
    {
        draft.VideoPath = string.Empty;
        draft.Title = string.Empty;
        draft.Description = string.Empty;
        draft.TagsCsv = string.Empty;
        draft.PresetId = null;
        draft.CategoryId = null;
        draft.Language = null;
        draft.MadeForKids = MadeForKidsSetting.Default;
        draft.CommentStatus = CommentStatusSetting.Default;
        draft.Transcript = string.Empty;
        draft.ChaptersText = string.Empty;
        draft.ThumbnailPath = string.Empty;
        draft.UploadState = UploadDraftUploadState.Pending;
        draft.UploadStatus = string.Empty;
        draft.UploadProgress = 0;
        draft.IsUploadProgressIndeterminate = true;
        ResetTranscriptionState(draft);
    }

    private static void ResetTranscriptionState(UploadDraft draft)
    {
        draft.TranscriptionState = UploadDraftTranscriptionState.None;
        draft.TranscriptionStatus = string.Empty;
        draft.TranscriptionProgress = 0;
        draft.IsTranscriptionProgressIndeterminate = true;
    }

    private void UpdateDraftActionStates()
    {
        UpdateUploadButtonState();
        UpdateTranscriptionButtonState();
    }

    private static bool ShouldAutoRemoveDraft(UploadDraft draft) =>
        draft.UploadState == UploadDraftUploadState.Completed &&
        (draft.TranscriptionState == UploadDraftTranscriptionState.None ||
         draft.TranscriptionState == UploadDraftTranscriptionState.Completed);

    private void TryAutoRemoveDraft(UploadDraft draft)
    {
        if (_settings.AutoRemoveCompletedDrafts != true)
        {
            return;
        }

        if (!_uploadDrafts.Contains(draft))
        {
            return;
        }

        if (!ShouldAutoRemoveDraft(draft))
        {
            return;
        }

        if (IsDraftTranscribing(draft))
        {
            return;
        }

        RemoveDraft(draft);
    }

    private void ApplyDraftPreferenceSettings()
    {
        if (_settings.RememberDraftsBetweenSessions != true)
        {
            _settings.SavedDrafts?.Clear();
            _settings.PendingTranscriptionQueue?.Clear();
        }
        else
        {
            ScheduleDraftPersistence();
        }
    }

    #endregion

    #region Video Drop Zone

    private readonly string[] _allowedVideoExtensions = { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm" };

    private bool IsAllowedVideoFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var ext = Path.GetExtension(path).ToLowerInvariant();
        return _allowedVideoExtensions.Contains(ext);
    }

    private static string NormalizeVideoPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    private bool IsVideoAlreadyInDrafts(string filePath, UploadDraft? ignoreDraft = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var normalizedPath = NormalizeVideoPath(filePath);
        return _uploadDrafts.Any(d =>
            d != ignoreDraft &&
            d.HasVideo &&
            string.Equals(NormalizeVideoPath(d.VideoPath), normalizedPath, StringComparison.OrdinalIgnoreCase));
    }

    private void AddVideoDrafts(IEnumerable<string> filePaths)
    {
        if (filePaths is null)
        {
            return;
        }

        UploadDraft? firstNewDraft = null;
        var addedAny = false;
        var newDrafts = new List<UploadDraft>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? firstDuplicateName = null;

        foreach (var filePath in filePaths)
        {
            if (!IsAllowedVideoFile(filePath))
            {
                continue;
            }

            if (!File.Exists(filePath))
            {
                continue;
            }

            var normalizedPath = NormalizeVideoPath(filePath);
            if (!seenPaths.Add(normalizedPath))
            {
                continue;
            }

            if (IsVideoAlreadyInDrafts(normalizedPath))
            {
                firstDuplicateName ??= Path.GetFileName(filePath);
                continue;
            }

            var draft = new UploadDraft
            {
                UploadStatus = "Bereit",
                TranscriptionStatus = string.Empty
            };

            ApplyCurrentUploadSettingsToDraft(draft);
            TryApplyDefaultPresetToDraft(draft);
            SetVideoFile(draft, normalizedPath, triggerAutoTranscribe: false);
            _uploadDrafts.Add(draft);
            firstNewDraft ??= draft;
            newDrafts.Add(draft);
            addedAny = true;
        }

        if (firstNewDraft is not null)
        {
            SetActiveDraft(firstNewDraft);
        }

        foreach (var draft in newDrafts)
        {
            var shouldAutoTranscribe = draft == firstNewDraft;
            _ = LoadVideoFileInfoAsync(draft, triggerAutoTranscribe: shouldAutoTranscribe);
        }

        UpdateDraftActionStates();

        if (firstDuplicateName is not null)
        {
            StatusTextBlock.Text = LocalizationHelper.Format("Status.Upload.VideoAlreadyInDraft", firstDuplicateName);
        }

        if (addedAny)
        {
            UpdateUploadListVisibility();
            ScheduleDraftPersistence();
        }
    }

    private void UploadDraftSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (UploadView.GetSelectedUploadItem() is UploadDraft draft)
        {
            SetActiveDraft(draft);
        }
        else
        {
            SetActiveDraft(null);
        }
    }

    private void AddVideosButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = LocalizationHelper.Get("Dialog.Video.Open.Filter"),
            Title = LocalizationHelper.Get("Dialog.Video.Open.Title"),
            Multiselect = true
        };

        if (!string.IsNullOrEmpty(_settings?.DefaultVideoFolder) &&
            Directory.Exists(_settings.DefaultVideoFolder))
        {
            dialog.InitialDirectory = _settings.DefaultVideoFolder;
        }

        if (dialog.ShowDialog() == true)
        {
            AddVideoDrafts(dialog.FileNames);
        }
    }

    private async void UploadAllButton_Click(object sender, RoutedEventArgs e)
    {
        var readyDrafts = _uploadDrafts.Where(d => d.HasVideo).ToList();
        if (readyDrafts.Count == 0)
        {
            _ui.Run(() =>
            {
                StatusTextBlock.Text = "Bitte zuerst ein Video auswaehlen.";
            }, UiUpdatePolicy.StatusPriority);
            return;
        }

        if (_settings.ConfirmBeforeUpload)
        {
            var confirmMessage = $"Sollen alle {readyDrafts.Count} Videos hochgeladen werden?";
            if (!ConfirmUpload(confirmMessage))
            {
                return;
            }
        }

        foreach (var draft in readyDrafts)
        {
            await UploadDraftAsync(draft).ConfigureAwait(false);
        }
    }

    private void TranscribeAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isTranscribeAllActive)
        {
            _ui.Run(() =>
            {
                CancelQueuedTranscriptions();
                CancelAllTranscriptions();
                _isTranscribeAllActive = false;
                UpdateTranscribeAllActionUi();
                StatusTextBlock.Text = LocalizationHelper.Get("Status.Transcription.Canceled");
            }, UiUpdatePolicy.StatusPriority);
            return;
        }

        var readyDrafts = _uploadDrafts.Where(d => d.HasVideo).ToList();
        if (readyDrafts.Count == 0)
        {
            _ui.Run(() =>
            {
                StatusTextBlock.Text = LocalizationHelper.Get("Status.Transcription.SelectVideo");
            }, UiUpdatePolicy.StatusPriority);
            return;
        }

        var queuedAny = false;
        foreach (var draft in readyDrafts)
        {
            if (draft is null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(draft.Transcript))
            {
                continue;
            }

            if (IsDraftTranscribing(draft) || IsDraftQueued(draft))
            {
                continue;
            }

            QueueDraftForTranscription(draft);
            queuedAny = true;
        }

        if (!queuedAny && ActiveTranscriptionCount == 0)
        {
            _ui.Run(() =>
            {
                StatusTextBlock.Text = LocalizationHelper.Get("Status.Transcription.SelectVideo");
            }, UiUpdatePolicy.StatusPriority);
            return;
        }

        _isTranscribeAllActive = true;
        _ui.Run(() =>
        {
            UpdateTranscribeAllActionUi();
            StatusTextBlock.Text = "Transkriptionen gestartet (max. 2 parallel).";
        }, UiUpdatePolicy.StatusPriority);
        _ = StartNextQueuedAutoTranscriptionAsync(ignoreAutoSetting: true);
    }

    private void RemoveDraftButton_Click(object sender, RoutedEventArgs e)
    {
        var draft = ResolveDraftFromSender(sender);
        if (draft is null)
        {
            return;
        }

        RemoveDraft(draft);
    }

    private void CancelUploadButton_Click(object sender, RoutedEventArgs e)
    {
        var draft = (sender as FrameworkElement)?.Tag as UploadDraft;
        if (draft is null)
        {
            return;
        }

        if (_isUploading && _activeUploadDraft == draft)
        {
            CancelActiveUpload();
        }
    }

    private async void UploadDraftButton_Click(object sender, RoutedEventArgs e)
    {
        var draft = ResolveDraftFromSender(sender);
        if (draft is null)
        {
            return;
        }

        if (_settings.ConfirmBeforeUpload)
        {
            if (!ConfirmUpload(LocalizationHelper.Get("Dialog.Upload.Confirm.Text")))
            {
                return;
            }
        }

        await UploadDraftAsync(draft).ConfigureAwait(false);
    }

    private async void TranscribeDraftButton_Click(object sender, RoutedEventArgs e)
    {
        var draft = ResolveDraftFromSender(sender);
        if (draft is null)
        {
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

        await StartTranscriptionAsync(draft).ConfigureAwait(false);
    }

    private async void TranscriptionQueuePrioritizeButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not UploadDraft draft)
        {
            return;
        }

        MoveDraftToFrontOfTranscriptionQueue(draft);
        await StartTranscriptionAsync(draft).ConfigureAwait(false);
    }

    private void TranscriptionQueueSkipButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not UploadDraft draft)
        {
            return;
        }

        RemoveDraftFromTranscriptionQueue(draft.Id);
        ResetTranscriptionState(draft);
        ScheduleDraftPersistence();
    }

    private async void FastFillSuggestionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isFastFillRunning)
        {
            _ui.Run(() => StatusTextBlock.Text = "Fast Fill laeuft bereits.");
            return;
        }

        var draft = ResolveDraftFromSender(sender);
        if (draft is null)
        {
            return;
        }

        if (_activeDraft != draft)
        {
            _ui.Run(() => SetActiveDraft(draft));
        }

        var button = sender as Button;
        _isFastFillRunning = true;
        if (button is not null)
        {
            _ui.Run(() => button.IsEnabled = false);
        }

        try
        {
            await FastFillSuggestionsAsync(draft).ConfigureAwait(false);
        }
        finally
        {
            _isFastFillRunning = false;
            if (button is not null)
            {
                _ui.Run(() => button.IsEnabled = true);
            }
        }
    }

    private async Task FastFillSuggestionsAsync(UploadDraft draft)
    {
        var draftAvailable = await _ui.RunAsync(() => _uploadDrafts.Contains(draft)).ConfigureAwait(false);
        if (!draftAvailable)
        {
            return;
        }

        await _ui.RunAsync(() =>
        {
            CloseSuggestionPopup();
            _settings.Persona ??= new ChannelPersona();
        }).ConfigureAwait(false);

        var transcriptText = await _ui.RunAsync(() => draft.Transcript).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(transcriptText))
        {
            await _ui.RunAsync(() => StatusTextBlock.Text = "Fast Fill: Transkription starten...").ConfigureAwait(false);
            await TryTranscribeForFastFillAsync(draft).ConfigureAwait(false);
            transcriptText = await _ui.RunAsync(() => draft.Transcript).ConfigureAwait(false);
        }

        var project = await _ui.RunAsync(
            () => BuildUploadProjectFromUi(includeScheduling: false, draftOverride: draft)).ConfigureAwait(false);
        ApplySuggestionLanguage(project);
        if (!string.IsNullOrWhiteSpace(transcriptText))
        {
            project.TranscriptText = transcriptText;
        }

        var cancellationToken = GetNewLlmCancellationToken();
        await _ui.RunAsync(() => StatusTextBlock.Text = "Fast Fill: Generiere Titel...").ConfigureAwait(false);

        try
        {
            var titles = await CollectSuggestionsAsync(
                () => _contentSuggestionService.SuggestTitlesAsync(project, _settings.Persona, cancellationToken),
                desiredCount: 1,
                maxRetries: 2,
                cancellationToken).ConfigureAwait(false);

            if (titles.Count > 0)
            {
                project.Title = titles[0];
                await _ui.RunAsync(() => UploadView.TitleTextBox.Text = titles[0]).ConfigureAwait(false);
            }

            await _ui.RunAsync(() => StatusTextBlock.Text = "Fast Fill: Generiere Beschreibung...").ConfigureAwait(false);
            var descriptions = await CollectSuggestionsAsync(
                () => _contentSuggestionService.SuggestDescriptionAsync(project, _settings.Persona, cancellationToken),
                desiredCount: 1,
                maxRetries: 2,
                cancellationToken).ConfigureAwait(false);

            if (descriptions.Count > 0)
            {
                await _ui.RunAsync(() => ApplyGeneratedDescription(descriptions[0])).ConfigureAwait(false);
            }

            project = await _ui.RunAsync(
                () => BuildUploadProjectFromUi(includeScheduling: false, draftOverride: draft)).ConfigureAwait(false);
            ApplySuggestionLanguage(project);
            await _ui.RunAsync(() => StatusTextBlock.Text = "Fast Fill: Generiere Tags...").ConfigureAwait(false);
            var tags = await _contentSuggestionService.SuggestTagsAsync(
                project,
                _settings.Persona,
                cancellationToken).ConfigureAwait(false);

            if (tags is not null && tags.Count > 0)
            {
                var suggestions = BuildTagSuggestionSets(tags, 1);
                if (suggestions.Count > 0)
                {
                    await _ui.RunAsync(() => UploadView.TagsTextBox.Text = suggestions[0]).ConfigureAwait(false);
                }
            }

            await _ui.RunAsync(() => StatusTextBlock.Text = "Fast Fill: Fertig.").ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await _ui.RunAsync(() => StatusTextBlock.Text = "Fast Fill: Abgebrochen.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _ui.RunAsync(() => StatusTextBlock.Text = $"Fast Fill: Fehler - {ex.Message}")
                .ConfigureAwait(false);
            _logger.Error($"Fast Fill failed: {ex.Message}", LlmLogSource, ex);
        }
        finally
        {
            _ui.Run(UpdateLogLinkIndicator);
        }
    }

    private UploadDraft? ResolveDraftFromSender(object sender)
    {
        return (sender as FrameworkElement)?.Tag as UploadDraft
               ?? (UploadView.GetSelectedUploadItem() as UploadDraft);
    }

    private async Task TryTranscribeForFastFillAsync(UploadDraft draft)
    {
        var videoPath = await _ui.RunAsync(() => draft.VideoPath).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
        {
            await _ui.RunAsync(
                () => StatusTextBlock.Text = LocalizationHelper.Get("Status.Transcription.SelectVideo"))
                .ConfigureAwait(false);
            return;
        }

        if (_transcriptionService is null)
        {
            await _ui.RunAsync(
                () => StatusTextBlock.Text = LocalizationHelper.Get("Status.Transcription.ServiceUnavailable"))
                .ConfigureAwait(false);
            return;
        }

        var isRunning = await _ui.RunAsync(() => IsDraftTranscribing(draft)).ConfigureAwait(false);
        if (isRunning)
        {
            await WaitForTranscriptionCompletionAsync(draft).ConfigureAwait(false);
            return;
        }

        await StartTranscriptionAsync(draft, allowQueue: false).ConfigureAwait(false);
    }

    private void SetActiveDraft(UploadDraft? draft)
    {
        if (_activeDraft == draft)
        {
            return;
        }

        _activeDraft = draft;
        _presetState.Reset();

        if (UploadView is null)
        {
            return;
        }

        _isLoadingDraft = true;
        try
        {
            if (!Equals(UploadView.GetSelectedUploadItem(), draft))
            {
                UploadView.SetSelectedUploadItem(draft);
            }

            if (draft is null)
            {
                ClearActiveDraftView();
            }
            else
            {
                PopulateActiveDraftView(draft);
            }
        }
        finally
        {
            _isLoadingDraft = false;
        }

        UpdateDraftActionStates();
        UpdateTranscriptionProgressUiForActiveDraft();
    }

    private void ClearActiveDraftView()
    {
        UploadView.VideoPathTextBox.Text = string.Empty;
        UploadView.VideoFileNameTextBlock.Text = LocalizationHelper.Get("Upload.VideoFileSize.Unknown");
        UploadView.VideoFileSizeTextBlock.Text = LocalizationHelper.Get("Upload.VideoFileSize.Unknown");
        ClearVideoInfoDisplay();
        ClearVideoPreviewDisplay();
        SetVideoDropState(false);
        UploadView.TitleTextBox.Text = string.Empty;
        UploadView.DescriptionTextBox.Text = string.Empty;
        UploadView.TagsTextBox.Text = string.Empty;
        UploadView.SelectCategoryById(null);
        UploadView.SelectLanguageByCode(null);
        UploadView.SetMadeForKids(MadeForKidsSetting.Default);
        UploadView.SetCommentStatus(CommentStatusSetting.Default);
        UploadView.PresetComboBox.SelectedItem = null;
        UploadView.TranscriptTextBox.Text = string.Empty;
        UploadView.ChaptersTextBox.Text = string.Empty;
        ClearThumbnailPreview();
    }

    private void PopulateActiveDraftView(UploadDraft draft)
    {
        UploadView.VideoPathTextBox.Text = draft.VideoPath;
        SetVideoDropState(draft.HasVideo);
        UploadView.VideoFileNameTextBlock.Text = string.IsNullOrWhiteSpace(draft.FileName)
            ? Path.GetFileName(draft.VideoPath)
            : draft.FileName;
        UploadView.VideoFileSizeTextBlock.Text = string.IsNullOrWhiteSpace(draft.FileSizeDisplay)
            ? FormatFileSize(0)
            : draft.FileSizeDisplay;
        UpdateVideoInfoDisplay(draft);
        UpdateVideoPreviewDisplay(draft);
        UploadView.TitleTextBox.Text = draft.Title;
        UploadView.DescriptionTextBox.Text = draft.Description;
        UploadView.TagsTextBox.Text = draft.TagsCsv;
        UploadView.TranscriptTextBox.Text = draft.Transcript;
        UploadView.ChaptersTextBox.Text = draft.ChaptersText;

        ApplyDraftThumbnailToUi(draft.ThumbnailPath);
        ApplyDraftUploadSettingsToUi(draft);

        if (draft.HasVideo && string.IsNullOrWhiteSpace(draft.VideoResolution))
        {
            _ = LoadVideoFileInfoAsync(draft, triggerAutoTranscribe: false);
        }
    }

    private static bool NeedsVideoInfo(UploadDraft draft)
    {
        if (draft is null)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(draft.VideoResolution)
               && string.IsNullOrWhiteSpace(draft.VideoDuration)
               && string.IsNullOrWhiteSpace(draft.VideoCodec);
    }

    private static bool NeedsVideoPreview(UploadDraft draft)
    {
        if (draft is null || string.IsNullOrWhiteSpace(draft.VideoPath))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(draft.VideoPreviewPath))
        {
            return true;
        }

        return !File.Exists(draft.VideoPreviewPath);
    }

    private async Task RefreshDraftVideoInfoAsync()
    {
        if (_uploadDrafts.Count == 0)
        {
            return;
        }

        var tasks = new List<Task>();
        foreach (var draft in _uploadDrafts)
        {
            if (!draft.HasVideo || !NeedsVideoInfo(draft))
            {
                continue;
            }

            if (!File.Exists(draft.VideoPath))
            {
                continue;
            }

            tasks.Add(LoadVideoFileInfoAsync(draft, triggerAutoTranscribe: false));
        }

        if (tasks.Count == 0)
        {
            return;
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task RefreshDraftVideoPreviewAsync()
    {
        if (_uploadDrafts.Count == 0)
        {
            return;
        }

        var tasks = new List<Task>();
        foreach (var draft in _uploadDrafts)
        {
            if (!draft.HasVideo || !NeedsVideoPreview(draft))
            {
                continue;
            }

            if (!File.Exists(draft.VideoPath))
            {
                continue;
            }

            tasks.Add(EnsureVideoPreviewAsync(draft, draft.VideoPath, null));
        }

        if (tasks.Count == 0)
        {
            return;
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private void SetVideoDropState(bool hasVideo)
    {
        UploadView.EmptyContentPanel.Visibility = hasVideo ? Visibility.Collapsed : Visibility.Visible;
        UploadView.VideoDropZone.Visibility = hasVideo ? Visibility.Visible : Visibility.Collapsed;
        UploadView.VideoDropSelectedState.Visibility = hasVideo ? Visibility.Visible : Visibility.Collapsed;
        UploadView.MainContentGrid.Visibility = hasVideo ? Visibility.Visible : Visibility.Collapsed;

        if (hasVideo)
        {
            UploadView.GetUploadContentScrollViewer()?.ScrollToTop();
        }
    }

    private void UpdateUploadListVisibility()
    {
        if (UploadView is null)
        {
            return;
        }

        var hasDrafts = _uploadDrafts.Count > 0;
        UploadView.UploadListPanel.Visibility = hasDrafts ? Visibility.Visible : Visibility.Collapsed;
        UploadView.UploadListDivider.Visibility = hasDrafts ? Visibility.Visible : Visibility.Collapsed;
        UploadView.UploadListColumn.Width = hasDrafts ? new GridLength(320) : new GridLength(0);
        UploadView.UploadListDividerColumn.Width = hasDrafts ? new GridLength(1) : new GridLength(0);
    }

    private void ApplyCurrentUploadSettingsToDraft(UploadDraft draft)
    {
        if (draft is null || UploadView is null)
        {
            return;
        }

        draft.Platform = UploadView.PlatformYouTubeToggle.IsChecked == true
            ? PlatformType.YouTube
            : PlatformType.YouTube;

        if (UploadView.PresetComboBox.SelectedItem is UploadPreset preset)
        {
            draft.PresetId = preset.Id;
        }
        else
        {
            draft.PresetId = null;
        }

        draft.Visibility = GetSelectedVisibilityFromUi();

        if (UploadView.PlaylistComboBox.SelectedItem is YouTubePlaylistInfo playlist)
        {
            draft.PlaylistId = playlist.Id;
            draft.PlaylistTitle = playlist.Title;
        }
        else
        {
            draft.PlaylistId = null;
            draft.PlaylistTitle = null;
        }

        var categoryId = UploadView.GetSelectedCategoryId();
        draft.CategoryId = string.IsNullOrWhiteSpace(categoryId) ? null : categoryId.Trim();
        draft.Language = UploadView.GetSelectedLanguageCode();
        draft.MadeForKids = GetSelectedMadeForKidsFromUi();
        draft.CommentStatus = GetSelectedCommentStatusFromUi();

        draft.ScheduleEnabled = UploadView.ScheduleCheckBox.IsChecked == true;
        draft.ScheduledDate = UploadView.ScheduleDatePicker.SelectedDate;
        draft.ScheduledTimeText = UploadView.ScheduleTimeTextBox.Text;
    }

    private void TryApplyDefaultPresetToDraft(UploadDraft draft)
    {
        if (draft is null || _settings.AutoApplyDefaultTemplate != true)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(draft.PresetId))
        {
            return;
        }

        var preset = GetDefaultPreset(draft.Platform);
        if (preset is null)
        {
            return;
        }

        ApplyPresetToDraft(draft, preset, overwriteExisting: true, applyDescriptionTemplate: true);
    }

    private void ApplyDraftUploadSettingsToUi(UploadDraft draft)
    {
        if (draft is null || UploadView is null)
        {
            return;
        }

        UploadView.PlatformYouTubeToggle.IsChecked = draft.Platform == PlatformType.YouTube;
        ApplyDraftPresetSelection(draft.PresetId);
        UploadView.SetDefaultVisibility(draft.Visibility);
        ApplyDraftPlaylistSelection(draft.PlaylistId);
        
        _categoryManager?.SelectById(draft.CategoryId);
        _languageManager?.SelectById(draft.Language);
        
        UploadView.SetMadeForKids(draft.MadeForKids);
        UploadView.SetCommentStatus(draft.CommentStatus);

        UploadView.ScheduleCheckBox.IsChecked = draft.ScheduleEnabled;

        if (draft.ScheduledDate.HasValue)
        {
            UploadView.ScheduleDatePicker.SelectedDate = draft.ScheduledDate.Value;
        }
        else if (UploadView.ScheduleDatePicker.SelectedDate is null)
        {
            UploadView.ScheduleDatePicker.SelectedDate = DateTime.Today.AddDays(1);
        }

        if (!string.IsNullOrWhiteSpace(draft.ScheduledTimeText))
        {
            UploadView.ScheduleTimeTextBox.Text = draft.ScheduledTimeText;
        }
        else if (string.IsNullOrWhiteSpace(UploadView.ScheduleTimeTextBox.Text))
        {
            UploadView.ScheduleTimeTextBox.Text = GetDefaultSchedulingTime().ToString(@"hh\:mm");
        }

        UpdateScheduleControlsEnabled();
    }

    private void ApplyDraftPresetSelection(string? presetId)
    {
        if (UploadView is null)
        {
            return;
        }

        var preset = FindPresetById(presetId);
        _isPresetBinding = true;
        UploadView.PresetComboBox.SelectedItem = preset;
        _isPresetBinding = false;
    }

    private void ApplyDraftPlaylistSelection(string? playlistId)
    {
        if (UploadView is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(playlistId))
        {
            playlistId = _settings.DefaultPlaylistId;
        }

        if (string.IsNullOrWhiteSpace(playlistId))
        {
            UploadView.PlaylistComboBox.SelectedItem = null;
            return;
        }

        foreach (var item in UploadView.PlaylistComboBox.Items)
        {
            if (item is YouTubePlaylistInfo playlist &&
                string.Equals(playlist.Id, playlistId, StringComparison.OrdinalIgnoreCase))
            {
                UploadView.PlaylistComboBox.SelectedItem = item;
                return;
            }
        }

        UploadView.PlaylistComboBox.SelectedItem = null;
    }

    private VideoVisibility GetSelectedVisibilityFromUi()
    {
        if (UploadView.VisibilityComboBox.SelectedItem is ComboBoxItem visItem &&
            visItem.Tag is VideoVisibility visEnum)
        {
            return visEnum;
        }

        return _settings.DefaultVisibility;
    }

    private MadeForKidsSetting GetSelectedMadeForKidsFromUi()
    {
        if (UploadView.MadeForKidsComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is MadeForKidsSetting setting)
        {
            return setting;
        }

        return MadeForKidsSetting.Default;
    }

    private CommentStatusSetting GetSelectedCommentStatusFromUi()
    {
        if (UploadView.CommentStatusComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is CommentStatusSetting setting)
        {
            return setting;
        }

        return CommentStatusSetting.Default;
    }

    private void ClearThumbnailPreview()
    {
        UploadView.ThumbnailPathTextBox.Text = string.Empty;
        UploadView.ThumbnailPreviewImage.Source = null;
        UploadView.ThumbnailEmptyState.Visibility = Visibility.Visible;
        UploadView.ThumbnailPreviewState.Visibility = Visibility.Collapsed;
    }

    private void ApplyDraftThumbnailToUi(string? thumbnailPath)
    {
        if (!string.IsNullOrWhiteSpace(thumbnailPath) && File.Exists(thumbnailPath))
        {
            UploadView.ThumbnailPathTextBox.Text = thumbnailPath;
            ApplyThumbnailToUi(thumbnailPath);
        }
        else
        {
            ClearThumbnailPreview();
        }
    }

    private void VideoDrop_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Handled = true;
            return;
        }

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files?.Length > 0 && files.All(IsAllowedVideoFile))
        {
            e.Effects = DragDropEffects.Copy;
            UploadView.VideoDropZone.BorderBrush = (SolidColorBrush)FindResource("PrimaryBrush");
        }

        e.Handled = true;
    }

    private void VideoDrop_DragLeave(object sender, DragEventArgs e)
    {
        UploadView.VideoDropZone.BorderBrush = (SolidColorBrush)FindResource("BorderBrush");
    }

    private void VideoDrop_Drop(object sender, DragEventArgs e)
    {
        UploadView.VideoDropZone.BorderBrush = (SolidColorBrush)FindResource("BorderBrush");

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            var validFiles = files?
                .Where(IsAllowedVideoFile)
                .ToList();

            if (validFiles is { Count: > 0 })
            {
                AddVideoDrafts(validFiles);
            }
        }
    }

    private void VideoDropZone_Click(object sender, MouseButtonEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = LocalizationHelper.Get("Dialog.Video.Open.Filter"),
            Title = LocalizationHelper.Get("Dialog.Video.Open.Title"),
            Multiselect = true
        };

        if (!string.IsNullOrEmpty(_settings?.DefaultVideoFolder) &&
            Directory.Exists(_settings.DefaultVideoFolder))
        {
            dialog.InitialDirectory = _settings.DefaultVideoFolder;
        }

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var fileNames = dialog.FileNames;
        if (fileNames is null || fileNames.Length == 0)
        {
            return;
        }

        var hasActiveVideo = _activeDraft is not null && _activeDraft.HasVideo;
        if (fileNames.Length == 1 && hasActiveVideo)
        {
            var replaceResult = MessageBox.Show(
                this,
                LocalizationHelper.Get("Dialog.Video.Replace.Text"),
                LocalizationHelper.Get("Dialog.Video.Replace.Title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (replaceResult == MessageBoxResult.Yes)
            {
                SetVideoFile(_activeDraft!, fileNames[0], triggerAutoTranscribe: true);
                _ = LoadVideoFileInfoAsync(_activeDraft!, triggerAutoTranscribe: true);
                return;
            }
        }

        AddVideoDrafts(fileNames);
    }

    private void SetVideoFile(UploadDraft draft, string filePath, bool triggerAutoTranscribe)
    {
        if (draft is null)
        {
            return;
        }

        var normalizedPath = NormalizeVideoPath(filePath);
        if (IsVideoAlreadyInDrafts(normalizedPath, ignoreDraft: draft))
        {
            StatusTextBlock.Text = LocalizationHelper.Format(
                "Status.Upload.VideoAlreadyInDraft",
                Path.GetFileName(filePath));
            return;
        }

        draft.VideoPath = normalizedPath;

        if (_activeDraft == draft)
        {
            UploadView.VideoPathTextBox.Text = normalizedPath;
            SetVideoDropState(true);
        }

        RememberLastVideoFolder(Path.GetDirectoryName(normalizedPath));

        UpdateDraftActionStates();

        var (fileName, _) = GetFileDisplayInfo(filePath);
        if (triggerAutoTranscribe && draft == _activeDraft)
        {
            _ = LoadVideoFileInfoAsync(draft, triggerAutoTranscribe: true);
        }

        ScheduleDraftPersistence();
    }

    private async Task LoadVideoFileInfoAsync(UploadDraft draft, bool triggerAutoTranscribe)
    {
        if (draft is null || string.IsNullOrWhiteSpace(draft.VideoPath))
        {
            return;
        }

        var filePath = draft.VideoPath;
        if (!File.Exists(filePath))
        {
            await _ui.RunAsync(() =>
            {
                ClearDraftVideoInfo(draft);
                if (_activeDraft == draft)
                {
                    UploadView.VideoFileNameTextBlock.Text = Path.GetFileName(filePath);
                    UploadView.VideoFileSizeTextBlock.Text = LocalizationHelper.Get("Upload.VideoFileSize.Unknown");
                    UpdateVideoInfoDisplay(draft);
                }
            });
            return;
        }

        await _videoInfoSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            var (fileName, fileSize) = await Task.Run(() => GetFileDisplayInfo(filePath)).ConfigureAwait(false);
            var mediaInfo = await TryGetVideoMediaInfoAsync(filePath).ConfigureAwait(false);

            draft.TranscriptionStatus ??= string.Empty;

            await _ui.RunAsync(() =>
            {
                UpdateDraftVideoInfo(draft, mediaInfo);
                if (_activeDraft == draft)
                {
                    UploadView.VideoFileNameTextBlock.Text = fileName;
                    UploadView.VideoFileSizeTextBlock.Text = FormatFileSize(fileSize);
                    UpdateVideoInfoDisplay(draft);
                }
            });

            _ = EnsureVideoPreviewAsync(draft, filePath, mediaInfo?.DurationSeconds);

            _logger.Info(
                LocalizationHelper.Format("Log.Upload.VideoSelected", fileName),
                UploadLogSource);

            if (triggerAutoTranscribe)
            {
                _ = TryAutoTranscribeAsync(draft);
            }

            ScheduleDraftPersistence();
        }
        catch (Exception ex)
        {
            _logger.Error(
                LocalizationHelper.Format("Log.Upload.VideoInfoError", ex.Message),
                UploadLogSource,
                ex);

            await _ui.RunAsync(() =>
            {
                ClearDraftVideoInfo(draft);
                if (_activeDraft == draft)
                {
                    UploadView.VideoFileNameTextBlock.Text = Path.GetFileName(filePath);
                    UploadView.VideoFileSizeTextBlock.Text = LocalizationHelper.Get("Upload.VideoFileSize.Unknown");
                    UpdateVideoInfoDisplay(draft);
                }
            });
        }
        finally
        {
            _videoInfoSemaphore.Release();
        }
    }

    private void UpdateDraftVideoInfo(UploadDraft draft, VideoMediaInfo? info)
    {
        if (draft is null)
        {
            return;
        }

        if (info is null)
        {
            ClearDraftVideoInfo(draft);
            return;
        }

        draft.VideoResolution = info.Resolution;
        draft.VideoFps = info.Fps;
        draft.VideoDuration = info.Duration;
        draft.VideoCodec = info.VideoCodec;
        draft.VideoBitrate = info.VideoBitrate;
        draft.AudioInfo = info.AudioInfo;
        draft.AudioBitrate = info.AudioBitrate;
    }

    private void ClearDraftVideoInfo(UploadDraft draft)
    {
        if (draft is null)
        {
            return;
        }

        draft.VideoResolution = null;
        draft.VideoFps = null;
        draft.VideoDuration = null;
        draft.VideoCodec = null;
        draft.VideoBitrate = null;
        draft.AudioInfo = null;
        draft.AudioBitrate = null;
    }

    private void ClearVideoInfoDisplay()
    {
        UpdateVideoInfoDisplay(null);
    }

    private void UpdateVideoInfoDisplay(UploadDraft? draft)
    {
        var unknown = LocalizationHelper.Get("Common.Unknown");

        UploadView.VideoResolutionTextBlock.Text = GetInfoOrUnknown(draft?.VideoResolution, unknown);
        UploadView.VideoFpsTextBlock.Text = GetInfoOrUnknown(draft?.VideoFps, unknown);
        UploadView.VideoDurationTextBlock.Text = GetInfoOrUnknown(draft?.VideoDuration, unknown);
        UploadView.VideoCodecTextBlock.Text = GetInfoOrUnknown(draft?.VideoCodec, unknown);
        UploadView.VideoBitrateTextBlock.Text = GetInfoOrUnknown(draft?.VideoBitrate, unknown);
        UploadView.VideoAudioTextBlock.Text = GetInfoOrUnknown(draft?.AudioInfo, unknown);
        UploadView.VideoAudioBitrateTextBlock.Text = GetInfoOrUnknown(draft?.AudioBitrate, unknown);
    }

    private static string GetInfoOrUnknown(string? value, string unknown)
    {
        return string.IsNullOrWhiteSpace(value) ? unknown : value;
    }

    private void ClearVideoPreviewDisplay()
    {
        UploadView.VideoPreviewImage.Source = null;
        UploadView.VideoPreviewImage.Visibility = Visibility.Collapsed;
        UploadView.VideoPreviewEmptyState.Visibility = Visibility.Visible;
    }

    private void UpdateVideoPreviewDisplay(UploadDraft? draft)
    {
        var previewPath = draft?.VideoPreviewPath;
        if (string.IsNullOrWhiteSpace(previewPath) || !File.Exists(previewPath))
        {
            ClearVideoPreviewDisplay();
            return;
        }

        var image = LoadImageSource(previewPath, VideoPreviewDecodePixelWidth);
        if (image is null)
        {
            ClearVideoPreviewDisplay();
            return;
        }

        UploadView.VideoPreviewImage.Source = image;
        UploadView.VideoPreviewImage.Visibility = Visibility.Visible;
        UploadView.VideoPreviewEmptyState.Visibility = Visibility.Collapsed;
    }

    private static ImageSource? LoadImageSource(string path, int decodePixelWidth)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (decodePixelWidth > 0)
            {
                image.DecodePixelWidth = decodePixelWidth;
            }
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private async Task EnsureVideoPreviewAsync(UploadDraft draft, string filePath, double? durationSeconds)
    {
        if (draft is null || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(draft.VideoPreviewPath) && File.Exists(draft.VideoPreviewPath))
        {
            await _ui.RunAsync(() =>
            {
                if (_activeDraft == draft)
                {
                    UpdateVideoPreviewDisplay(draft);
                }
            });
            return;
        }

        var ffmpegPath = GetFfmpegPath();
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            return;
        }

        var previewPath = BuildVideoPreviewPath(filePath);
        if (File.Exists(previewPath))
        {
            draft.VideoPreviewPath = previewPath;
            await _ui.RunAsync(() =>
            {
                if (_activeDraft == draft)
                {
                    UpdateVideoPreviewDisplay(draft);
                }
            });
            return;
        }

        await _videoPreviewSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (File.Exists(previewPath))
            {
                draft.VideoPreviewPath = previewPath;
                await _ui.RunAsync(() =>
                {
                    if (_activeDraft == draft)
                    {
                        UpdateVideoPreviewDisplay(draft);
                    }
                });
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(previewPath)!);

            var seekSeconds = GetPreviewTimestamp(durationSeconds);
            var success = await Task.Run(() => TryGeneratePreview(ffmpegPath, filePath, previewPath, seekSeconds)).ConfigureAwait(false);
            if (!success || !File.Exists(previewPath))
            {
                return;
            }

            draft.VideoPreviewPath = previewPath;
            await _ui.RunAsync(() =>
            {
                if (_activeDraft == draft)
                {
                    UpdateVideoPreviewDisplay(draft);
                }
            });
        }
        finally
        {
            _videoPreviewSemaphore.Release();
        }
    }

    private string? GetFfmpegPath()
    {
        var status = _transcriptionService?.GetDependencyStatus();
        if (!string.IsNullOrWhiteSpace(status?.FFmpegPath))
        {
            return status.FFmpegPath;
        }

        return OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
    }

    private static string BuildVideoPreviewPath(string filePath)
    {
        var info = new FileInfo(filePath);
        var key = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key));
        var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        var folder = Constants.VideoPreviewFolder;
        return Path.Combine(folder, $"{hash}.jpg");
    }

    private static double GetPreviewTimestamp(double? durationSeconds)
    {
        if (!durationSeconds.HasValue || durationSeconds <= 0)
        {
            return 1;
        }

        var duration = durationSeconds.Value;
        if (duration < 1)
        {
            return 0.1;
        }

        var target = duration * 0.1;
        target = Math.Clamp(target, 1, Math.Min(duration - 0.1, 8));
        return target;
    }

    private static bool TryGeneratePreview(string ffmpegPath, string filePath, string previewPath, double seekSeconds)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                RedirectStandardOutput = false,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-v");
            startInfo.ArgumentList.Add("error");
            startInfo.ArgumentList.Add("-ss");
            startInfo.ArgumentList.Add(seekSeconds.ToString("0.###", CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(filePath);
            startInfo.ArgumentList.Add("-frames:v");
            startInfo.ArgumentList.Add("1");
            startInfo.ArgumentList.Add("-vf");
            startInfo.ArgumentList.Add("scale=320:-1:force_original_aspect_ratio=decrease");
            startInfo.ArgumentList.Add("-q:v");
            startInfo.ArgumentList.Add("2");
            startInfo.ArgumentList.Add("-an");
            startInfo.ArgumentList.Add("-sn");
            startInfo.ArgumentList.Add("-dn");
            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add(previewPath);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            var stderrTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(8000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best effort.
                }

                return false;
            }

            _ = stderrTask.GetAwaiter().GetResult();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<VideoMediaInfo?> TryGetVideoMediaInfoAsync(string filePath)
    {
        var ffprobePath = GetFfprobePath();
        if (string.IsNullOrWhiteSpace(ffprobePath))
        {
            return null;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ffprobePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-v");
            startInfo.ArgumentList.Add("error");
            startInfo.ArgumentList.Add("-print_format");
            startInfo.ArgumentList.Add("json");
            startInfo.ArgumentList.Add("-show_entries");
            startInfo.ArgumentList.Add("format=duration,bit_rate:stream=codec_type,codec_name,width,height,avg_frame_rate,r_frame_rate,bit_rate,sample_rate,channels");
            startInfo.ArgumentList.Add("-show_format");
            startInfo.ArgumentList.Add("-show_streams");
            startInfo.ArgumentList.Add(filePath);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var waitTask = process.WaitForExitAsync();
            var completed = await Task.WhenAny(waitTask, Task.Delay(8000)).ConfigureAwait(false);
            if (completed != waitTask)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best effort.
                }

                var stderrTimeout = await errorTask.ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(stderrTimeout))
                {
                    _logger.Debug($"ffprobe timeout: {stderrTimeout}", UploadLogSource);
                }

                return null;
            }

            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                var stderr = errorTask.Result;
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    _logger.Debug($"ffprobe failed: {stderr}", UploadLogSource);
                }
                return null;
            }

            var json = outputTask.Result;
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return ParseFfprobeJson(json);
        }
        catch
        {
            return null;
        }
    }

    private string? GetFfprobePath()
    {
        var status = _transcriptionService?.GetDependencyStatus();
        if (!string.IsNullOrWhiteSpace(status?.FFprobePath))
        {
            return status.FFprobePath;
        }

        if (!string.IsNullOrWhiteSpace(status?.FFmpegPath))
        {
            var ffprobeName = GetFfprobeExecutableName();
            var directory = Path.GetDirectoryName(status.FFmpegPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                var candidate = Path.Combine(directory, ffprobeName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return GetFfprobeExecutableName();
    }

    private static string GetFfprobeExecutableName()
        => OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";

    private static VideoMediaInfo? ParseFfprobeJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("streams", out var streamsElement) ||
            streamsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        JsonElement? videoStream = null;
        JsonElement? audioStream = null;

        foreach (var stream in streamsElement.EnumerateArray())
        {
            var codecType = GetString(stream, "codec_type");
            if (videoStream is null && string.Equals(codecType, "video", StringComparison.OrdinalIgnoreCase))
            {
                videoStream = stream;
            }
            else if (audioStream is null && string.Equals(codecType, "audio", StringComparison.OrdinalIgnoreCase))
            {
                audioStream = stream;
            }
        }

        var formatElement = doc.RootElement.TryGetProperty("format", out var formatValue)
            ? formatValue
            : default;

        var width = GetInt(videoStream, "width");
        var height = GetInt(videoStream, "height");
        var resolution = width.HasValue && height.HasValue ? $"{width}x{height}" : null;

        var fps = ParseFps(GetString(videoStream, "avg_frame_rate"))
                  ?? ParseFps(GetString(videoStream, "r_frame_rate"));
        var fpsText = fps.HasValue ? fps.Value.ToString("0.##", CultureInfo.CurrentCulture) : null;

        var durationSeconds = GetDouble(formatElement, "duration") ?? GetDouble(videoStream, "duration");
        var durationText = durationSeconds.HasValue
            ? FormatDuration(TimeSpan.FromSeconds(durationSeconds.Value))
            : null;

        var videoCodec = FormatCodecName(GetString(videoStream, "codec_name"));
        var videoBitrate = FormatBitrate(GetBitRate(videoStream) ?? GetBitRate(formatElement));

        var audioCodec = FormatCodecName(GetString(audioStream, "codec_name"));
        var sampleRate = GetInt(audioStream, "sample_rate");
        var channels = GetInt(audioStream, "channels");
        var audioInfo = FormatAudioInfo(audioCodec, sampleRate, channels);
        var audioBitrate = FormatBitrate(GetBitRate(audioStream));

        return new VideoMediaInfo(
            resolution,
            fpsText,
            durationText,
            durationSeconds,
            videoCodec,
            videoBitrate,
            audioInfo,
            audioBitrate);
    }

    private static string? GetString(JsonElement? element, string propertyName)
    {
        return element.HasValue ? GetString(element.Value, propertyName) : null;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static int? GetInt(JsonElement? element, string propertyName)
    {
        return element.HasValue ? GetInt(element.Value, propertyName) : null;
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
        {
            return value;
        }

        if (property.ValueKind == JsonValueKind.String &&
            int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static double? GetDouble(JsonElement? element, string propertyName)
    {
        return element.HasValue ? GetDouble(element.Value, propertyName) : null;
    }

    private static double? GetDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var value))
        {
            return value;
        }

        if (property.ValueKind == JsonValueKind.String &&
            double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static long? GetBitRate(JsonElement? element)
    {
        return element.HasValue ? GetBitRate(element.Value) : null;
    }

    private static long? GetBitRate(JsonElement element)
    {
        if (!element.TryGetProperty("bit_rate", out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var value))
        {
            return value;
        }

        if (property.ValueKind == JsonValueKind.String &&
            long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static double? ParseFps(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "0/0")
        {
            return null;
        }

        var parts = value.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) &&
            denominator > 0)
        {
            return numerator / denominator;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
        {
            return numeric;
        }

        return null;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}";
        }

        return $"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private static string? FormatCodecName(string? codecName)
    {
        if (string.IsNullOrWhiteSpace(codecName))
        {
            return null;
        }

        return codecName.ToLowerInvariant() switch
        {
            "h264" => "H.264",
            "h265" => "H.265",
            "hevc" => "HEVC",
            "av1" => "AV1",
            _ => codecName.ToUpperInvariant()
        };
    }

    private static string? FormatBitrate(long? bitsPerSecond)
    {
        if (!bitsPerSecond.HasValue || bitsPerSecond <= 0)
        {
            return null;
        }

        var bps = bitsPerSecond.Value;
        if (bps >= 1_000_000)
        {
            return $"{bps / 1_000_000d:0.#} Mbps";
        }

        if (bps >= 1_000)
        {
            return $"{bps / 1_000d:0.#} Kbps";
        }

        return $"{bps} bps";
    }

    private static string? FormatAudioInfo(string? codec, int? sampleRate, int? channels)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(codec))
        {
            parts.Add(codec);
        }

        if (sampleRate.HasValue && sampleRate.Value > 0)
        {
            parts.Add($"{sampleRate.Value / 1000d:0.#} kHz");
        }

        if (channels.HasValue && channels.Value > 0)
        {
            parts.Add(GetChannelLabel(channels.Value));
        }

        return parts.Count == 0 ? null : string.Join(" / ", parts);
    }

    private static string GetChannelLabel(int channels)
    {
        return channels switch
        {
            1 => "Mono",
            2 => "Stereo",
            _ => $"{channels}ch"
        };
    }

    private sealed record VideoMediaInfo(
        string? Resolution,
        string? Fps,
        string? Duration,
        double? DurationSeconds,
        string? VideoCodec,
        string? VideoBitrate,
        string? AudioInfo,
        string? AudioBitrate);

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }

    private static (string FileName, long FileSize) GetFileDisplayInfo(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return (string.Empty, 0);
        }

        try
        {
            var info = new FileInfo(filePath);
            var fileSize = info.Exists ? info.Length : 0;
            return (info.Name, fileSize);
        }
        catch
        {
            return (Path.GetFileName(filePath) ?? string.Empty, 0);
        }
    }

    private void RememberLastVideoFolder(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        if (string.Equals(_settings.LastVideoFolder, directory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _settings.LastVideoFolder = directory;
        ScheduleSettingsSave();
    }

    private void TitleTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingDraft)
        {
            return;
        }

        if (_activeDraft is not null)
        {
            _activeDraft.Title = UploadView.TitleTextBox.Text ?? string.Empty;
        }

        UpdateUploadButtonState();
        ScheduleDraftPersistenceDebounced();
    }

    private void DescriptionTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSettingDescriptionText)
        {
            return;
        }

        if (_isLoadingDraft)
        {
            return;
        }

        var current = UploadView.DescriptionTextBox.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(current))
        {
            _presetState.Reset();
            return;
        }

        _presetState.UpdateBaseDescriptionIfChanged(current);

        if (_activeDraft is not null)
        {
            _activeDraft.Description = current;
        }

        ScheduleDraftPersistence();
    }

    private void TagsTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingDraft)
        {
            return;
        }

        if (_activeDraft is not null)
        {
            _activeDraft.TagsCsv = UploadView.TagsTextBox.Text ?? string.Empty;
        }

        ScheduleDraftPersistence();
    }

    private void TranscriptTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingDraft)
        {
            return;
        }

        if (_activeDraft is not null)
        {
            _activeDraft.Transcript = UploadView.TranscriptTextBox.Text ?? string.Empty;
        }

        ScheduleDraftPersistenceDebounced();
    }

    private void ChaptersTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingDraft)
        {
            return;
        }

        if (_activeDraft is not null)
        {
            _activeDraft.ChaptersText = UploadView.ChaptersTextBox.Text ?? string.Empty;
        }

        ScheduleDraftPersistenceDebounced();
    }

    private void PlatformYouTubeToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoadingDraft)
        {
            return;
        }

        if (_activeDraft is not null)
        {
            _activeDraft.Platform = PlatformType.YouTube;
            ScheduleDraftPersistence();
        }
    }

    private void PlatformYouTubeToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_isLoadingDraft)
        {
            return;
        }

        if (_activeDraft is not null)
        {
            _activeDraft.Platform = PlatformType.YouTube;
            ScheduleDraftPersistence();
        }
    }

    private void VisibilityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingDraft)
        {
            return;
        }

        if (_activeDraft is not null)
        {
            _activeDraft.Visibility = GetSelectedVisibilityFromUi();
            ScheduleDraftPersistence();
        }
    }

    private void PlaylistComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingDraft)
        {
            return;
        }

        if (_activeDraft is null)
        {
            return;
        }

        if (UploadView.PlaylistComboBox.SelectedItem is YouTubePlaylistInfo playlist)
        {
            _activeDraft.PlaylistId = playlist.Id;
            _activeDraft.PlaylistTitle = playlist.Title;
        }
        else
        {
            _activeDraft.PlaylistId = null;
            _activeDraft.PlaylistTitle = null;
        }

        ScheduleDraftPersistenceDebounced();
    }

    private void CategoryManager_SelectionChanged(object? sender, CategoryOption? selected)
    {
        if (_isLoadingDraft) return;

        if (_activeDraft is not null)
        {
            var categoryId = selected?.Id;
            _activeDraft.CategoryId = string.IsNullOrWhiteSpace(categoryId) ? null : categoryId.Trim();
        }

        ScheduleDraftPersistence();
    }

    private void UploadLanguageManager_SelectionChanged(object? sender, LanguageOption? selected)
    {
        if (_isLoadingDraft) return;

        if (_activeDraft is not null)
        {
            var code = selected?.Code;
            _activeDraft.Language = string.IsNullOrWhiteSpace(code) ? null : code;
        }

        ScheduleDraftPersistence();
    }

    private void MadeForKidsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingDraft)
        {
            return;
        }

        if (_activeDraft is not null)
        {
            _activeDraft.MadeForKids = GetSelectedMadeForKidsFromUi();
        }

        ScheduleDraftPersistenceDebounced();
    }

    private void CommentStatusComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingDraft)
        {
            return;
        }

        if (_activeDraft is not null)
        {
            _activeDraft.CommentStatus = GetSelectedCommentStatusFromUi();
        }

        ScheduleDraftPersistence();
    }

    private void ScheduleCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        UpdateScheduleControlsEnabled();

        if (_isLoadingDraft)
        {
            return;
        }

        if (_activeDraft is not null)
        {
            _activeDraft.ScheduleEnabled = UploadView.ScheduleCheckBox.IsChecked == true;
            _activeDraft.ScheduledDate = UploadView.ScheduleDatePicker.SelectedDate;
            _activeDraft.ScheduledTimeText = UploadView.ScheduleTimeTextBox.Text;
            ScheduleDraftPersistenceDebounced();
        }
    }

    private void ScheduleCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        UpdateScheduleControlsEnabled();

        if (_isLoadingDraft)
        {
            return;
        }

        if (_activeDraft is not null)
        {
            _activeDraft.ScheduleEnabled = UploadView.ScheduleCheckBox.IsChecked == true;
            _activeDraft.ScheduledDate = UploadView.ScheduleDatePicker.SelectedDate;
            _activeDraft.ScheduledTimeText = UploadView.ScheduleTimeTextBox.Text;
            ScheduleDraftPersistence();
        }
    }

    private void ScheduleDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingDraft)
        {
            return;
        }

        if (_activeDraft is not null)
        {
            _activeDraft.ScheduledDate = UploadView.ScheduleDatePicker.SelectedDate;
            ScheduleDraftPersistence();
        }
    }

    private void ScheduleTimeTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingDraft)
        {
            return;
        }

        if (_activeDraft is not null)
        {
            _activeDraft.ScheduledTimeText = UploadView.ScheduleTimeTextBox.Text;
            ScheduleDraftPersistence();
        }
    }

    private void FocusTargetOnContainerClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d)
        {
            while (d != null)
            {
                if (d is TextBoxBase || d is PasswordBox)
                {
                    return;
                }

                if (d is ButtonBase)
                {
                    return;
                }

                d = VisualTreeHelper.GetParent(d);
            }
        }

        if (sender is FrameworkElement fe && fe.Tag is Control target && target.Focusable)
        {
            target.Focus();

            if (target is TextBox tb)
            {
                tb.CaretIndex = tb.Text?.Length ?? 0;
            }

            e.Handled = true;
        }
    }

    private void UpdateUploadButtonState()
    {
        var hasVideo = _activeDraft?.HasVideo == true;
        var hasTitle = !string.IsNullOrWhiteSpace(_activeDraft?.Title);

        var canUploadSingle = hasVideo && hasTitle && !_isUploading;
        UploadView.UploadButton.IsEnabled = canUploadSingle;
        var anyVideoDraft = _uploadDrafts.Any(d => d.HasVideo);
        if (UploadAllActionButton is not null)
        {
            UploadAllActionButton.IsEnabled = !_isUploading && anyVideoDraft;
        }
    }

    #endregion

    #region Thumbnail Drop Zone

    private readonly string[] _allowedImageExtensions = { ".png", ".jpg", ".jpeg" };

    private void ThumbnailDrop_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files?.Length == 1)
            {
                var ext = Path.GetExtension(files[0]).ToLowerInvariant();
                if (_allowedImageExtensions.Contains(ext))
                {
                    e.Effects = DragDropEffects.Copy;
                    UploadView.ThumbnailDropZone.BorderBrush = (SolidColorBrush)FindResource("PrimaryBrush");
                }
            }
        }
        e.Handled = true;
    }

    private void ThumbnailDrop_DragLeave(object sender, DragEventArgs e)
    {
        UploadView.ThumbnailDropZone.BorderBrush = (SolidColorBrush)FindResource("BorderBrush");
    }

    private void ThumbnailDrop_Drop(object sender, DragEventArgs e)
    {
        UploadView.ThumbnailDropZone.BorderBrush = (SolidColorBrush)FindResource("BorderBrush");

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files?.Length == 1)
            {
                var ext = Path.GetExtension(files[0]).ToLowerInvariant();
                if (_allowedImageExtensions.Contains(ext))
                {
                    SetThumbnailFile(files[0]);
                }
            }
        }
    }

    private void ThumbnailDropZone_Click(object sender, MouseButtonEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = LocalizationHelper.Get("Dialog.Thumbnail.Open.Filter"),
            Title = LocalizationHelper.Get("Dialog.Thumbnail.Open.Title")
        };

        if (!string.IsNullOrEmpty(_settings?.DefaultThumbnailFolder) &&
            Directory.Exists(_settings.DefaultThumbnailFolder))
        {
            dialog.InitialDirectory = _settings.DefaultThumbnailFolder;
        }

        if (dialog.ShowDialog() == true)
        {
            SetThumbnailFile(dialog.FileName);
        }
    }

    private void SetThumbnailFile(string filePath)
    {
        var storedPath = PersistThumbnail(filePath, _activeDraft);
        UploadView.ThumbnailPathTextBox.Text = storedPath;
        if (_activeDraft is not null)
        {
            _activeDraft.ThumbnailPath = storedPath;
        }

        var (fileName, _) = GetFileDisplayInfo(storedPath);
        UploadView.ThumbnailFileNameTextBlock.Text = fileName;

        if (ApplyThumbnailToUi(storedPath))
        {
            _logger.Info(
                LocalizationHelper.Format("Log.Upload.ThumbnailSelected", fileName),
                UploadLogSource);
        }

        ScheduleDraftPersistence();
    }

    private string PersistThumbnail(string filePath, UploadDraft? targetDraft)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return filePath;
        }

        try
        {
            var fullPath = Path.GetFullPath(filePath);
            var storageFolder = Constants.ThumbnailsFolder;

            if (fullPath.StartsWith(storageFolder, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath;
            }

            var extension = Path.GetExtension(fullPath);
            var fileName = targetDraft is not null
                ? $"{targetDraft.Id}{extension}"
                : Path.GetFileName(fullPath);

            var targetPath = Path.Combine(storageFolder, fileName);
            File.Copy(fullPath, targetPath, overwrite: true);
            return targetPath;
        }
        catch (Exception ex)
        {
            _logger.Debug($"Failed to persist thumbnail: {ex.Message}", UploadLogSource);
            return filePath;
        }
    }

    private bool TryPersistDraftThumbnail(UploadDraft draft)
    {
        if (draft is null || string.IsNullOrWhiteSpace(draft.ThumbnailPath))
        {
            return false;
        }

        if (!File.Exists(draft.ThumbnailPath))
        {
            return false;
        }

        var storageFolder = Constants.ThumbnailsFolder;
        var fullPath = Path.GetFullPath(draft.ThumbnailPath);
        if (fullPath.StartsWith(storageFolder, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var storedPath = PersistThumbnail(fullPath, draft);
        if (string.Equals(storedPath, fullPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        draft.ThumbnailPath = storedPath;
        return true;
    }

    private bool TryResolveDraftThumbnailFromVideo(UploadDraft draft)
    {
        if (draft is null || !string.IsNullOrWhiteSpace(draft.ThumbnailPath))
        {
            return false;
        }

        if (!draft.HasVideo || string.IsNullOrWhiteSpace(draft.VideoPath))
        {
            return false;
        }

        var videoPath = draft.VideoPath;
        var baseName = Path.GetFileNameWithoutExtension(videoPath);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return false;
        }

        var searchDirs = new List<string>();
        var videoDir = Path.GetDirectoryName(videoPath);
        if (!string.IsNullOrWhiteSpace(videoDir) && Directory.Exists(videoDir))
        {
            searchDirs.Add(videoDir);
        }

        if (!string.IsNullOrWhiteSpace(_settings.DefaultThumbnailFolder) &&
            Directory.Exists(_settings.DefaultThumbnailFolder) &&
            !searchDirs.Contains(_settings.DefaultThumbnailFolder, StringComparer.OrdinalIgnoreCase))
        {
            searchDirs.Add(_settings.DefaultThumbnailFolder);
        }

        if (searchDirs.Count == 0)
        {
            return false;
        }

        var suffixes = new[] { string.Empty, "_thumb", "-thumb", "_thumbnail", "-thumbnail" };
        var extensions = new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };

        foreach (var dir in searchDirs)
        {
            foreach (var suffix in suffixes)
            {
                foreach (var extension in extensions)
                {
                    var candidate = Path.Combine(dir, $"{baseName}{suffix}{extension}");
                    if (!File.Exists(candidate))
                    {
                        continue;
                    }

                    var storedPath = PersistThumbnail(candidate, draft);
                    draft.ThumbnailPath = storedPath;
                    if (_activeDraft == draft)
                    {
                        ApplyDraftThumbnailToUi(storedPath);
                    }
                    return true;
                }
            }
        }

        return false;
    }

    private bool ApplyThumbnailToUi(string filePath)
    {
        try
        {
            var image = LoadImageSource(filePath, ThumbnailDecodePixelWidth);
            if (image is null)
            {
                return false;
            }

            UploadView.ThumbnailPreviewImage.Source = image;

            UploadView.ThumbnailEmptyState.Visibility = Visibility.Collapsed;
            UploadView.ThumbnailPreviewState.Visibility = Visibility.Visible;
            UploadView.ThumbnailFileNameTextBlock.Text = Path.GetFileName(filePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(
                LocalizationHelper.Format("Log.Upload.ThumbnailLoadError", ex.Message),
                UploadLogSource,
                ex);
            return false;
        }
    }

    private void ThumbnailClearButton_Click(object sender, RoutedEventArgs e)
    {
        ClearThumbnailPreview();
        if (_activeDraft is not null)
        {
            _activeDraft.ThumbnailPath = string.Empty;
        }

        ScheduleDraftPersistence();
    }

    private void VideoChangeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeDraft is null)
        {
            VideoDropZone_Click(sender, null!);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = LocalizationHelper.Get("Dialog.Video.Open.Filter"),
            Title = LocalizationHelper.Get("Dialog.Video.Open.Title"),
            Multiselect = false
        };

        if (!string.IsNullOrEmpty(_settings?.DefaultVideoFolder) &&
            Directory.Exists(_settings.DefaultVideoFolder))
        {
            dialog.InitialDirectory = _settings.DefaultVideoFolder;
        }

        if (dialog.ShowDialog() == true)
        {
            SetVideoFile(_activeDraft, dialog.FileName, triggerAutoTranscribe: true);
            _ = LoadVideoFileInfoAsync(_activeDraft, triggerAutoTranscribe: true);
        }
    }

    #endregion

    #region Upload Actions

    private void ApplyPresetButton_Click(object sender, RoutedEventArgs e)
    {
        UploadPreset? preset = UploadView.PresetComboBox.SelectedItem as UploadPreset;

        if (preset is null && PresetsPageView?.SelectedPreset is UploadPreset tabPreset)
        {
            preset = tabPreset;
        }

        if (preset is null)
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Status.Preset.NoneSelected");
            return;
        }

        ApplyPresetToActiveDraft(preset, overwriteExisting: true, applyDescriptionTemplate: true);

        StatusTextBlock.Text = LocalizationHelper.Format("Status.Preset.Applied", preset.Name);

        _logger.Info(
            LocalizationHelper.Format("Log.Preset.Applied", preset.Name),
            PresetLogSource);
    }

    private async void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeDraft is null)
        {
            StatusTextBlock.Text = "Bitte zuerst ein Video auswaehlen.";
            return;
        }

        if (_settings.ConfirmBeforeUpload)
        {
            if (!ConfirmUpload(LocalizationHelper.Get("Dialog.Upload.Confirm.Text")))
            {
                return;
            }
        }

        await UploadDraftAsync(_activeDraft).ConfigureAwait(false);
    }

    private bool ConfirmUpload(string message)
    {
        var result = MessageBox.Show(
            this,
            message,
            LocalizationHelper.Get("Dialog.Upload.Confirm.Title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        var confirmed = result == MessageBoxResult.Yes;
        if (!confirmed)
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Status.Upload.Canceled");
        }

        return confirmed;
    }

    private async Task UploadDraftAsync(UploadDraft draft)
    {
        if (draft is null || !draft.HasVideo)
        {
            _ui.Run(() => StatusTextBlock.Text = "Bitte zuerst ein Video auswaehlen.");
            return;
        }

        if (_activeDraft != draft)
        {
            _ui.Run(() => SetActiveDraft(draft));
        }

        var project = await _ui.RunAsync(
            () => BuildUploadProjectFromUi(includeScheduling: true, draftOverride: draft)).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(project.ThumbnailPath) &&
            !File.Exists(project.ThumbnailPath))
        {
            await _ui.RunAsync(
                () => StatusTextBlock.Text = LocalizationHelper.Get("Status.Upload.ThumbnailNotFound"))
                .ConfigureAwait(false);
            _logger.Warning(
                LocalizationHelper.Format("Log.Upload.ThumbnailMissing", project.ThumbnailPath ?? string.Empty),
                UploadLogSource);
            project.ThumbnailPath = string.Empty;
        }
        else if (!string.IsNullOrWhiteSpace(project.ThumbnailPath))
        {
            var fileInfo = new FileInfo(project.ThumbnailPath);
            if (fileInfo.Length > Constants.MaxThumbnailSizeBytes)
            {
                await _ui.RunAsync(
                    () => StatusTextBlock.Text = LocalizationHelper.Get("Status.Upload.ThumbnailTooLarge"))
                    .ConfigureAwait(false);
                var thumbnailSizeMb = fileInfo.Length / (1024.0 * 1024.0);
                _logger.Warning(
                    LocalizationHelper.Format("Log.Upload.ThumbnailTooLarge", thumbnailSizeMb),
                    UploadLogSource);
                project.ThumbnailPath = string.Empty;
            }
        }

        try
        {
            project.Validate();
        }
        catch (Exception ex)
        {
            var validationText = LocalizationHelper.Format("Status.Upload.ValidationFailed", ex.Message);
            await _ui.RunAsync(() =>
            {
                UpdateUploadStatus(draft, UploadDraftUploadState.Failed, validationText);
                ScheduleDraftPersistence();
            }).ConfigureAwait(false);
            _logger.Warning(
                LocalizationHelper.Format("Log.Upload.ValidationFailed", ex.Message),
                UploadLogSource);
            return;
        }

        if (project.Platform == PlatformType.YouTube && !_youTubeClient.IsConnected)
        {
            await _ui.RunAsync(
                () => StatusTextBlock.Text = LocalizationHelper.Get("Status.Upload.ConnectYouTube"))
                .ConfigureAwait(false);
            return;
        }

        var uploadCts = new CancellationTokenSource();
        await _ui.RunAsync(() =>
        {
            _activeUploadCts = uploadCts;
            _activeUploadDraft = draft;
            _isUploading = true;
            UpdateUploadButtonState();
        }).ConfigureAwait(false);
        var cancellationToken = uploadCts.Token;

        var preparingText = LocalizationHelper.Get("Status.Upload.Preparing");
        await _ui.RunAsync(() =>
        {
            UpdateUploadStatus(
                draft,
                UploadDraftUploadState.Uploading,
                preparingText,
                isProgressIndeterminate: true,
                progressPercent: 0);
            ScheduleDraftPersistence();
        }).ConfigureAwait(false);

        _logger.Info(
            LocalizationHelper.Format("Log.Upload.Started", project.Title),
            UploadLogSource);
        ShowUploadProgress(preparingText);

        var progressReporter = new Progress<UploadProgressInfo>(info =>
        {
            ReportUploadProgress(info);
            _ui.Run(() =>
            {
                if (!string.IsNullOrWhiteSpace(info.Message))
                {
                    draft.UploadStatus = info.Message;
                }

                var percent = info.IsIndeterminate
                    ? 0
                    : (double.IsNaN(info.Percent) ? 0 : Math.Clamp(info.Percent, 0, 100));

                draft.IsUploadProgressIndeterminate = info.IsIndeterminate;
                draft.UploadProgress = percent;
                ScheduleDraftPersistence();
            });
        });

        try
        {
            var selectedPreset = await _ui.RunAsync(() =>
            {
                if (string.IsNullOrWhiteSpace(draft.PresetId))
                {
                    return null;
                }

                return _loadedPresets.FirstOrDefault(p =>
                    string.Equals(p.Id, draft.PresetId, StringComparison.OrdinalIgnoreCase));
            }).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var result = await _uploadService.UploadAsync(
                project,
                selectedPreset,
                progressReporter,
                cancellationToken).ConfigureAwait(false);

            await Task.Run(() => _uploadHistoryService.AddEntry(project, result)).ConfigureAwait(false);
            await LoadUploadHistoryAsync().ConfigureAwait(false);

            if (result.Success)
            {
                var videoUrlText = result.VideoUrl?.ToString() ?? string.Empty;
                var successText = LocalizationHelper.Format("Status.Upload.Success", videoUrlText);
                await _ui.RunAsync(() => UpdateUploadStatus(draft, UploadDraftUploadState.Completed, successText))
                    .ConfigureAwait(false);
                _logger.Info(
                    LocalizationHelper.Format("Log.Upload.Success", videoUrlText),
                    UploadLogSource);

                if (_settings.OpenBrowserAfterUpload && result.VideoUrl is not null)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(result.VideoUrl.ToString())
                        {
                            UseShellExecute = true
                        });
                    }
                    catch
                    {
                        // Browser oeffnen ist Komfort
                    }
                }

                await _ui.RunAsync(() =>
                {
                    TryAutoRemoveDraft(draft);
                    ScheduleDraftPersistence();
                }).ConfigureAwait(false);
            }
            else
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    var canceledText = LocalizationHelper.Get("Status.Upload.Canceled");
                    await _ui.RunAsync(() =>
                    {
                        UpdateUploadStatus(
                            draft,
                            UploadDraftUploadState.Pending,
                            canceledText,
                            isProgressIndeterminate: false,
                            progressPercent: 0);
                        ScheduleDraftPersistence();
                    }).ConfigureAwait(false);
                    _logger.Info("Upload canceled.", UploadLogSource);
                    return;
                }

                var errorText = result.ErrorMessage ?? string.Empty;
                var failure = LocalizationHelper.Format("Status.Upload.Failed", errorText);
                await _ui.RunAsync(() =>
                {
                    UpdateUploadStatus(draft, UploadDraftUploadState.Failed, failure);
                    ScheduleDraftPersistence();
                }).ConfigureAwait(false);
                _logger.Error(
                    LocalizationHelper.Format("Log.Upload.Failed", errorText),
                    UploadLogSource);
            }
        }
        catch (OperationCanceledException)
        {
            var canceledText = LocalizationHelper.Get("Status.Upload.Canceled");
            await _ui.RunAsync(() =>
            {
                UpdateUploadStatus(
                    draft,
                    UploadDraftUploadState.Pending,
                    canceledText,
                    isProgressIndeterminate: false,
                    progressPercent: 0);
                ScheduleDraftPersistence();
            }).ConfigureAwait(false);
            _logger.Info("Upload canceled.", UploadLogSource);
        }
        catch (Exception ex)
        {
            var unexpected = LocalizationHelper.Format("Status.Upload.UnexpectedError", ex.Message);
            await _ui.RunAsync(() =>
            {
                UpdateUploadStatus(draft, UploadDraftUploadState.Failed, unexpected);
                ScheduleDraftPersistence();
            }).ConfigureAwait(false);
            _logger.Error(
                LocalizationHelper.Format("Log.Upload.UnexpectedError", ex.Message),
                UploadLogSource,
                ex);
        }
        finally
        {
            await _ui.RunAsync(() =>
            {
                if (ReferenceEquals(_activeUploadCts, uploadCts))
                {
                    _activeUploadCts = null;
                }

                if (_activeUploadDraft == draft)
                {
                    _activeUploadDraft = null;
                }

                _isUploading = false;
                HideUploadProgress();
                UpdateUploadButtonState();
                UpdateLogLinkIndicator();
                ScheduleDraftPersistence();
            }).ConfigureAwait(false);

            uploadCts.Dispose();
        }
    }


    #endregion

    #region Upload Progress UI

    private void ShowUploadProgress(string message)
    {
        _ui.Run(() =>
        {
            UploadProgressBar.Visibility = Visibility.Visible;
            UploadProgressLabel.Visibility = Visibility.Visible;

            UploadProgressBar.IsIndeterminate = true;
            UploadProgressBar.Value = 0;
            UploadProgressLabel.Text = message;
        }, UiUpdatePolicy.ProgressPriority);
    }

    private void ReportUploadProgress(UploadProgressInfo info)
    {
        _uploadProgressThrottle.Post(() => ApplyUploadProgress(info));
    }

    private void ApplyUploadProgress(UploadProgressInfo info)
    {
        UploadProgressBar.Visibility = Visibility.Visible;
        UploadProgressLabel.Visibility = Visibility.Visible;

        UploadProgressBar.IsIndeterminate = info.IsIndeterminate;

        if (!info.IsIndeterminate)
        {
            var percent = double.IsNaN(info.Percent) ? 0 : Math.Clamp(info.Percent, 0, 100);
            UploadProgressBar.Value = percent;
        }

        if (!string.IsNullOrWhiteSpace(info.Message))
        {
            UploadProgressLabel.Text = info.Message;
            StatusTextBlock.Text = info.Message;
        }
    }

    private void HideUploadProgress()
    {
        _uploadProgressThrottle.CancelPending();
        _ui.Run(() =>
        {
            UploadProgressBar.Visibility = Visibility.Collapsed;
            UploadProgressLabel.Visibility = Visibility.Collapsed;
            UploadProgressBar.IsIndeterminate = false;
            UploadProgressBar.Value = 0;
            UploadProgressLabel.Text = string.Empty;
        }, UiUpdatePolicy.ProgressPriority);
    }

    private void UpdateUploadStatus(
        UploadDraft draft,
        UploadDraftUploadState state,
        string statusText,
        bool? isProgressIndeterminate = null,
        double? progressPercent = null,
        bool updateStatusText = true)
    {
        draft.UploadState = state;
        draft.UploadStatus = statusText;

        if (isProgressIndeterminate.HasValue)
        {
            draft.IsUploadProgressIndeterminate = isProgressIndeterminate.Value;
        }

        if (progressPercent.HasValue)
        {
            draft.UploadProgress = progressPercent.Value;
        }

        if (updateStatusText)
        {
            StatusTextBlock.Text = statusText;
        }
    }

    #endregion

    #region Upload Helpers

    private void ApplyPresetToActiveDraft(UploadPreset preset, bool overwriteExisting, bool applyDescriptionTemplate)
    {
        if (_activeDraft is null || UploadView is null)
        {
            return;
        }

        ApplyPresetToDraft(_activeDraft, preset, overwriteExisting, applyDescriptionTemplate);
        ApplyPresetToUi(_activeDraft, preset);
        ScheduleDraftPersistence();
    }

    private void ApplyPresetToDraft(UploadDraft draft, UploadPreset preset, bool overwriteExisting, bool applyDescriptionTemplate)
    {
        if (draft is null || preset is null)
        {
            return;
        }

        draft.PresetId = preset.Id;

        if (!string.IsNullOrWhiteSpace(preset.TitlePrefix))
        {
            if (!draft.Title.StartsWith(preset.TitlePrefix, StringComparison.OrdinalIgnoreCase))
            {
                draft.Title = string.IsNullOrWhiteSpace(draft.Title)
                    ? preset.TitlePrefix
                    : $"{preset.TitlePrefix}{draft.Title}";
            }
        }

        if (!string.IsNullOrWhiteSpace(preset.TagsCsv))
        {
            if (string.IsNullOrWhiteSpace(draft.TagsCsv))
            {
                draft.TagsCsv = preset.TagsCsv;
            }
            else if (overwriteExisting || !string.IsNullOrWhiteSpace(draft.TagsCsv))
            {
                draft.TagsCsv = MergeTagsCsv(draft.TagsCsv, preset.TagsCsv);
            }
        }

        if (overwriteExisting || draft.Visibility == _settings.DefaultVisibility)
        {
            draft.Visibility = preset.Visibility;
        }

        if (overwriteExisting || string.IsNullOrWhiteSpace(draft.PlaylistId))
        {
            draft.PlaylistId = preset.PlaylistId;
            draft.PlaylistTitle = preset.PlaylistTitle;
        }

        if (overwriteExisting || string.IsNullOrWhiteSpace(draft.CategoryId))
        {
            draft.CategoryId = preset.CategoryId;
        }

        if (overwriteExisting || string.IsNullOrWhiteSpace(draft.Language))
        {
            draft.Language = preset.Language;
        }

        if (overwriteExisting || draft.MadeForKids == MadeForKidsSetting.Default)
        {
            draft.MadeForKids = preset.MadeForKids;
        }

        if (overwriteExisting || draft.CommentStatus == CommentStatusSetting.Default)
        {
            draft.CommentStatus = preset.CommentStatus;
        }

        if (applyDescriptionTemplate && !string.IsNullOrWhiteSpace(preset.DescriptionTemplate))
        {
            if (overwriteExisting || string.IsNullOrWhiteSpace(draft.Description))
            {
                var project = BuildUploadProjectFromUi(includeScheduling: true, draftOverride: draft);

                string baseDescription;
                var hasPlaceholder = PresetHasDescriptionPlaceholder(preset);
                if (hasPlaceholder && _presetState.TryGetBaseDescription(preset, out var storedBase))
                {
                    baseDescription = storedBase;
                }
                else if (hasPlaceholder && _presetState.TryGetBaseDescription(out var fallbackBase))
                {
                    baseDescription = fallbackBase;
                }
                else
                {
                    baseDescription = string.Empty;
                }

                project.Description = baseDescription;
                var result = _templateService.ApplyTemplate(preset.DescriptionTemplate, project);

                _presetState.Record(
                    preset,
                    baseDescription,
                    result,
                    hasPlaceholder);

                draft.Description = result;
            }
        }
    }

    private void ApplyPresetToUi(UploadDraft draft, UploadPreset preset)
    {
        if (UploadView is null)
        {
            return;
        }

        UploadView.TitleTextBox.Text = draft.Title;
        UploadView.TagsTextBox.Text = draft.TagsCsv;
        UploadView.SetDefaultVisibility(draft.Visibility);
        UploadView.SelectCategoryById(draft.CategoryId);
        UploadView.SelectLanguageByCode(draft.Language);
        UploadView.SetMadeForKids(draft.MadeForKids);
        UploadView.SetCommentStatus(draft.CommentStatus);
        ApplyDraftPlaylistSelection(draft.PlaylistId);

        if (preset is not null && UploadView.PresetComboBox.SelectedItem != preset)
        {
            _isPresetBinding = true;
            UploadView.PresetComboBox.SelectedItem = preset;
            _isPresetBinding = false;
        }

        if (!string.IsNullOrWhiteSpace(draft.Description))
        {
            SetDescriptionText(draft.Description);
        }
    }

    private static string MergeTagsCsv(string existingTags, string presetTags)
    {
        var combined = new List<string>();
        AddTags(combined, existingTags);
        AddTags(combined, presetTags);
        return string.Join(", ", combined);
    }

    private static void AddTags(List<string> target, string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return;
        }

        var tags = csv.Split(',')
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t));

        foreach (var tag in tags)
        {
            if (!target.Any(existing => string.Equals(existing, tag, StringComparison.OrdinalIgnoreCase)))
            {
                target.Add(tag);
            }
        }
    }

    private UploadProject BuildUploadProjectFromUi(bool includeScheduling, UploadDraft? draftOverride = null)
    {
        var platform = draftOverride?.Platform ?? PlatformType.YouTube;
        if (draftOverride is null && UploadView.PlatformYouTubeToggle.IsChecked == true)
        {
            platform = PlatformType.YouTube;
        }

        var visibility = draftOverride is not null
            ? draftOverride.Visibility
            : GetSelectedVisibilityFromUi();

        string? playlistId;
        string? playlistTitle = null;
        if (draftOverride is not null)
        {
            playlistId = string.IsNullOrWhiteSpace(draftOverride.PlaylistId)
                ? _settings.DefaultPlaylistId
                : draftOverride.PlaylistId;
            playlistTitle = draftOverride.PlaylistTitle;
        }
        else if (UploadView.PlaylistComboBox.SelectedItem is YouTubePlaylistInfo plItem)
        {
            playlistId = plItem.Id;
            playlistTitle = plItem.Title;
        }
        else
        {
            playlistId = _settings.DefaultPlaylistId;
        }

        DateTimeOffset? scheduledTime = null;
        var scheduleEnabled = draftOverride?.ScheduleEnabled ?? (UploadView.ScheduleCheckBox.IsChecked == true);
        if (includeScheduling && scheduleEnabled)
        {
            DateTime? scheduledDate;
            string? timeText;
            if (draftOverride is not null)
            {
                scheduledDate = draftOverride.ScheduledDate ?? DateTime.Today.AddDays(1);
                timeText = draftOverride.ScheduledTimeText;
            }
            else
            {
                scheduledDate = UploadView.ScheduleDatePicker.SelectedDate;
                timeText = UploadView.ScheduleTimeTextBox.Text;
            }

            if (scheduledDate is DateTime date)
            {
                if (string.IsNullOrWhiteSpace(timeText))
                {
                    timeText = GetDefaultSchedulingTime().ToString(@"hh\:mm");
                }

                if (!TimeSpan.TryParse(timeText, CultureInfo.CurrentCulture, out var timeOfDay))
                {
                    timeOfDay = GetDefaultSchedulingTime();
                }

                var localDateTime = date.Date + timeOfDay;
                var offset = TimeZoneInfo.Local.GetUtcOffset(localDateTime);
                scheduledTime = new DateTimeOffset(localDateTime, offset);
            }
        }

        string? categoryId;
        string? language;
        MadeForKidsSetting madeForKidsSetting;
        CommentStatusSetting commentStatusSetting;
        if (draftOverride is not null)
        {
            categoryId = draftOverride.CategoryId;
            language = draftOverride.Language;
            madeForKidsSetting = draftOverride.MadeForKids;
            commentStatusSetting = draftOverride.CommentStatus;
        }
        else
        {
            categoryId = UploadView.GetSelectedCategoryId();
            language = UploadView.GetSelectedLanguageCode();
            madeForKidsSetting = GetSelectedMadeForKidsFromUi();
            commentStatusSetting = GetSelectedCommentStatusFromUi();
        }

        var project = new UploadProject
        {
            VideoFilePath = draftOverride?.VideoPath ?? UploadView.VideoPathTextBox.Text ?? string.Empty,
            Title = draftOverride?.Title ?? UploadView.TitleTextBox.Text ?? string.Empty,
            Description = draftOverride?.Description ?? UploadView.DescriptionTextBox.Text ?? string.Empty,
            Platform = platform,
            Visibility = visibility,
            PlaylistId = playlistId,
            PlaylistTitle = playlistTitle,
            ScheduledTime = scheduledTime,
            ThumbnailPath = draftOverride?.ThumbnailPath ?? UploadView.ThumbnailPathTextBox.Text,
            TranscriptText = draftOverride?.Transcript ?? UploadView.TranscriptTextBox.Text,
            ChaptersText = draftOverride?.ChaptersText ?? UploadView.ChaptersTextBox.Text,
            CategoryId = string.IsNullOrWhiteSpace(categoryId) ? null : categoryId.Trim(),
            Language = string.IsNullOrWhiteSpace(language) ? null : language.Trim(),
            MadeForKids = ToNullableBool(madeForKidsSetting),
            CommentStatus = commentStatusSetting
        };

        project.SetTagsFromCsv(draftOverride?.TagsCsv ?? (UploadView.TagsTextBox.Text ?? string.Empty));

        return project;
    }

    private void UpdateScheduleControlsEnabled()
    {
        var enabled = UploadView.ScheduleCheckBox.IsChecked == true;
        UploadView.ScheduleDatePicker.IsEnabled = enabled;
        UploadView.ScheduleTimeTextBox.IsEnabled = enabled;
    }

    private TimeSpan GetDefaultSchedulingTime()
    {
        if (!string.IsNullOrWhiteSpace(_settings.DefaultSchedulingTime) &&
            TimeSpan.TryParse(_settings.DefaultSchedulingTime, CultureInfo.CurrentCulture, out var parsed))
        {
            return parsed;
        }

        return TimeSpan.Parse(Constants.DefaultSchedulingTime);
    }

    private static bool? ToNullableBool(MadeForKidsSetting setting) =>
        setting switch
        {
            MadeForKidsSetting.Yes => true,
            MadeForKidsSetting.No => false,
            _ => null
        };

    private void SetDescriptionText(string newText)
    {
        try
        {
            _isSettingDescriptionText = true;
            UploadView.DescriptionTextBox.Text = newText?.Trim() ?? string.Empty;
        }
        finally
        {
            _isSettingDescriptionText = false;
        }
    }

    private void AppendDescriptionText(string newText)
    {
        var existing = UploadView.DescriptionTextBox.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(existing))
        {
            SetDescriptionText(newText);
            return;
        }

        if (string.IsNullOrWhiteSpace(newText))
        {
            return;
        }

        try
        {
            _isSettingDescriptionText = true;
            UploadView.DescriptionTextBox.Text =
                $"{existing.TrimEnd()}{Environment.NewLine}{Environment.NewLine}{newText.Trim()}";
        }
        finally
        {
            _isSettingDescriptionText = false;
        }
    }

    private static bool PresetHasDescriptionPlaceholder(UploadPreset preset)
    {
        if (preset is null || string.IsNullOrWhiteSpace(preset.DescriptionTemplate))
        {
            return false;
        }

        return DescriptionPlaceholderRegex.IsMatch(preset.DescriptionTemplate);
    }

    private static bool PresetHasChaptersPlaceholder(UploadPreset preset)
    {
        if (preset is null || string.IsNullOrWhiteSpace(preset.DescriptionTemplate))
        {
            return false;
        }

        return ChaptersPlaceholderRegex.IsMatch(preset.DescriptionTemplate);
    }

    private void ApplyGeneratedDescription(string newDescription)
    {
        if (_presetState.Preset is not null && _presetState.HasDescriptionPlaceholder)
        {
            var project = BuildUploadProjectFromUi(includeScheduling: true);
            project.Description = newDescription ?? string.Empty;
            var applied = _templateService.ApplyTemplate(_presetState.Preset.DescriptionTemplate, project);
            _presetState.UpdateLastResult(applied);
            SetDescriptionText(applied);
        }
        else
        {
            AppendDescriptionText(newDescription);
        }
    }

    private void ApplyGeneratedChapters(string chaptersText)
    {
        UploadView.ChaptersTextBox.Text = chaptersText ?? string.Empty;

        if (_presetState.Preset is null || !PresetHasChaptersPlaceholder(_presetState.Preset))
        {
            return;
        }

        var project = BuildUploadProjectFromUi(includeScheduling: true);

        if (PresetHasDescriptionPlaceholder(_presetState.Preset))
        {
            if (_presetState.TryGetBaseDescription(_presetState.Preset, out var storedBase))
            {
                project.Description = storedBase;
            }
            else if (_presetState.TryGetBaseDescription(out var fallbackBase))
            {
                project.Description = fallbackBase;
            }
        }

        var applied = _templateService.ApplyTemplate(_presetState.Preset.DescriptionTemplate, project);
        _presetState.UpdateLastResult(applied);
        SetDescriptionText(applied);
    }

    #endregion
}
