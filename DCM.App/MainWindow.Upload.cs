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
using System.Windows.Threading;
using System.Text.RegularExpressions;
using DCM.App.Models;
using DCM.Core;
using DCM.Core.Configuration;
using DCM.Core.Models;
using DCM.YouTube;
using Microsoft.Win32;

namespace DCM.App;

public partial class MainWindow
{
    private const string UploadLogSource = "Upload";
    private const string TemplateLogSource = "Template";

    #region Draft Persistence

    private void RestoreDraftsFromSettings()
    {
        _uploadDrafts.Clear();

        if (_settings.RememberDraftsBetweenSessions != true)
        {
            _settings.SavedDrafts?.Clear();
            return;
        }

        var snapshots = _settings.SavedDrafts ?? new List<UploadDraftSnapshot>();
        if (snapshots.Count == 0)
        {
            return;
        }

        _isRestoringDrafts = true;
        var removedDuringRestore = false;
        var autoRemoveCompleted = _settings.AutoRemoveCompletedDrafts == true;

        try
        {
            _transcriptionQueue.Clear();

            foreach (var snapshot in snapshots)
            {
                var draft = UploadDraft.FromSnapshot(snapshot);

                if (autoRemoveCompleted && ShouldAutoRemoveDraft(draft))
                {
                    removedDuringRestore = true;
                    continue;
                }

                _uploadDrafts.Add(draft);
            }

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
                    candidate.TranscriptionState = UploadDraftTranscriptionState.Pending;
                    candidate.TranscriptionStatus = LocalizationHelper.Get("Status.Transcription.Queued");
                    candidate.IsTranscriptionProgressIndeterminate = true;
                    candidate.TranscriptionProgress = 0;
                }
            }

            if (_uploadDrafts.Count > 0)
            {
                SetActiveDraft(_uploadDrafts[0]);
            }

            if (removedDuringRestore)
            {
                PersistDrafts();
            }
        }
        finally
        {
            _isRestoringDrafts = false;
        }
    }

    private void ScheduleDraftPersistence()
    {
        if (_isRestoringDrafts || _settings.RememberDraftsBetweenSessions != true)
        {
            return;
        }

        _draftPersistenceDirty = true;

        if (_draftPersistenceTimer is null)
        {
            _draftPersistenceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _draftPersistenceTimer.Tick += DraftPersistenceTimer_Tick;
        }

        _draftPersistenceTimer.Stop();
        _draftPersistenceTimer.Start();
    }

    private void DraftPersistenceTimer_Tick(object? sender, EventArgs e)
    {
        _draftPersistenceTimer?.Stop();

        if (_draftPersistenceDirty)
        {
            PersistDrafts();
        }
    }

    private void PersistDrafts()
    {
        _draftPersistenceDirty = false;

        if (_settings.RememberDraftsBetweenSessions != true)
        {
            _settings.SavedDrafts?.Clear();
            _settings.PendingTranscriptionQueue?.Clear();
            SaveSettings();
            return;
        }

        var snapshots = _uploadDrafts
            .Select(d => d.ToSnapshot())
            .ToList();

        _settings.SavedDrafts = snapshots;
        _settings.PendingTranscriptionQueue = _transcriptionQueue.ToList();
        SaveSettings();
    }

    private void RemoveDraft(UploadDraft draft)
    {
        if (_isTranscribing && _activeTranscriptionDraft == draft)
        {
            CancelTranscription();
        }

        if (_isUploading && _activeUploadDraft == draft)
        {
            CancelActiveUpload();
        }

        RemoveDraftFromTranscriptionQueue(draft.Id);
        var wasActive = draft == _activeDraft;
        _uploadDrafts.Remove(draft);
        ResetDraftState(draft);

        if (wasActive)
        {
            SetActiveDraft(_uploadDrafts.LastOrDefault());
        }

        UpdateUploadButtonState();
        UpdateTranscriptionButtonState();
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
        draft.Transcript = string.Empty;
        draft.ThumbnailPath = string.Empty;
        draft.UploadState = UploadDraftUploadState.Pending;
        draft.UploadStatus = string.Empty;
        draft.TranscriptionState = UploadDraftTranscriptionState.None;
        draft.TranscriptionStatus = string.Empty;
        draft.UploadProgress = 0;
        draft.TranscriptionProgress = 0;
        draft.IsUploadProgressIndeterminate = true;
        draft.IsTranscriptionProgressIndeterminate = true;
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

        if (_isTranscribing && _activeTranscriptionDraft == draft)
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

    private void AddVideoDrafts(IEnumerable<string> filePaths)
    {
        if (filePaths is null)
        {
            return;
        }

        UploadDraft? firstNewDraft = null;
        var addedAny = false;

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

            var draft = new UploadDraft
            {
                UploadStatus = "Bereit",
                TranscriptionStatus = string.Empty
            };

            SetVideoFile(draft, filePath, triggerAutoTranscribe: false);
            _uploadDrafts.Add(draft);
            firstNewDraft ??= draft;
            addedAny = true;
        }

        if (firstNewDraft is not null)
        {
            SetActiveDraft(firstNewDraft);
            _ = LoadVideoFileInfoAsync(firstNewDraft, triggerAutoTranscribe: true);
        }

        UpdateUploadButtonState();
        UpdateTranscriptionButtonState();

        if (addedAny)
        {
            ScheduleDraftPersistence();
        }
    }

    private void UploadItemsListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
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
            StatusTextBlock.Text = "Bitte zuerst ein Video auswaehlen.";
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
            await UploadDraftAsync(draft);
        }
    }

    private async void TranscribeAllButton_Click(object sender, RoutedEventArgs e)
    {
        var readyDrafts = _uploadDrafts.Where(d => d.HasVideo).ToList();
        if (readyDrafts.Count == 0)
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Status.Transcription.SelectVideo");
            return;
        }

        foreach (var draft in readyDrafts)
        {
            if (draft is null)
            {
                continue;
            }
            await StartTranscriptionAsync(draft!);
        }
    }

    private void RemoveDraftButton_Click(object sender, RoutedEventArgs e)
    {
        var draft = (sender as FrameworkElement)?.Tag as UploadDraft
                    ?? (UploadView.GetSelectedUploadItem() as UploadDraft);

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

    private async void TranscriptionPrioritizeButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not UploadDraft draft)
        {
            return;
        }

        MoveDraftToFrontOfTranscriptionQueue(draft);

        if (!_isTranscribing)
        {
            await StartTranscriptionAsync(draft);
        }
    }

    private void TranscriptionSkipButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not UploadDraft draft)
        {
            return;
        }

        RemoveDraftFromTranscriptionQueue(draft.Id);
        draft.TranscriptionState = UploadDraftTranscriptionState.None;
        draft.TranscriptionStatus = string.Empty;
        draft.TranscriptionProgress = 0;
        draft.IsTranscriptionProgressIndeterminate = true;
        ScheduleDraftPersistence();
    }

    private void SetActiveDraft(UploadDraft? draft)
    {
        if (_activeDraft == draft)
        {
            return;
        }

        _activeDraft = draft;

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

        UpdateUploadButtonState();
        UpdateTranscriptionButtonState();
    }

    private void ClearActiveDraftView()
    {
        UploadView.VideoPathTextBox.Text = string.Empty;
        UploadView.VideoFileNameTextBlock.Text = LocalizationHelper.Get("Upload.VideoFileSize.Unknown");
        UploadView.VideoFileSizeTextBlock.Text = LocalizationHelper.Get("Upload.VideoFileSize.Unknown");
        SetVideoDropState(false);
        UploadView.TitleTextBox.Text = string.Empty;
        UploadView.DescriptionTextBox.Text = string.Empty;
        UploadView.TagsTextBox.Text = string.Empty;
        UploadView.TranscriptTextBox.Text = string.Empty;
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
        UploadView.TitleTextBox.Text = draft.Title;
        UploadView.DescriptionTextBox.Text = draft.Description;
        UploadView.TagsTextBox.Text = draft.TagsCsv;
        UploadView.TranscriptTextBox.Text = draft.Transcript;

        ApplyDraftThumbnailToUi(draft.ThumbnailPath);
    }

    private void SetVideoDropState(bool hasVideo)
    {
        UploadView.VideoDropEmptyState.Visibility = hasVideo ? Visibility.Collapsed : Visibility.Visible;
        UploadView.VideoDropSelectedState.Visibility = hasVideo ? Visibility.Visible : Visibility.Collapsed;
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

        draft.VideoPath = filePath;

        if (_activeDraft == draft)
        {
            UploadView.VideoPathTextBox.Text = filePath;
            SetVideoDropState(true);
        }

        RememberLastVideoFolder(Path.GetDirectoryName(filePath));

        UpdateUploadButtonState();
        UpdateTranscriptionButtonState();

        var (fileName, _) = GetFileDisplayInfo(filePath);
        _logger.Info(
            LocalizationHelper.Format("Log.Upload.VideoSelected", fileName),
            UploadLogSource);

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
        try
        {
            var (fileName, fileSize) = await Task.Run(() => GetFileDisplayInfo(filePath));

            draft.TranscriptionStatus ??= string.Empty;

            await OnUiThreadAsync(() =>
            {
                if (_activeDraft == draft)
                {
                    UploadView.VideoFileNameTextBlock.Text = fileName;
                    UploadView.VideoFileSizeTextBlock.Text = FormatFileSize(fileSize);
                }
            });

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

            await OnUiThreadAsync(() =>
            {
                if (_activeDraft == draft)
                {
                    UploadView.VideoFileNameTextBlock.Text = Path.GetFileName(filePath);
                    UploadView.VideoFileSizeTextBlock.Text = LocalizationHelper.Get("Upload.VideoFileSize.Unknown");
                }
            });
        }
    }

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
        ScheduleDraftPersistence();
    }

    private void DescriptionTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSettingDescriptionText)
        {
            return;
        }

        var current = UploadView.DescriptionTextBox.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(current))
        {
            _templateState.Reset();
            return;
        }

        _templateState.UpdateBaseDescriptionIfChanged(current);

        if (_isLoadingDraft)
        {
            return;
        }

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

        ScheduleDraftPersistence();
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
        UploadView.UploadAllButton.IsEnabled = !_isUploading && anyVideoDraft;
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
        UploadView.ThumbnailPathTextBox.Text = filePath;
        if (_activeDraft is not null)
        {
            _activeDraft.ThumbnailPath = filePath;
        }

        var (fileName, _) = GetFileDisplayInfo(filePath);
        UploadView.ThumbnailFileNameTextBlock.Text = fileName;

        if (ApplyThumbnailToUi(filePath))
        {
            _logger.Info(
                LocalizationHelper.Format("Log.Upload.ThumbnailSelected", fileName),
                UploadLogSource);
        }

        ScheduleDraftPersistence();
    }

    private bool ApplyThumbnailToUi(string filePath)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            UploadView.ThumbnailPreviewImage.Source = bitmap;

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

    private void ApplyTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        Template? tmpl = UploadView.TemplateComboBox.SelectedItem as Template;

        if (tmpl is null && TemplatesPageView?.SelectedTemplate is Template tabTemplate)
        {
            tmpl = tabTemplate;
        }

        if (tmpl is null)
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Status.Template.NoneSelected");
            return;
        }

        var project = BuildUploadProjectFromUi(includeScheduling: true);

        string baseDescription;
        if (_templateState.TryGetBaseDescription(tmpl, out var storedBase))
        {
            baseDescription = storedBase;
            project.Description = storedBase;
        }
        else
        {
            baseDescription = UploadView.DescriptionTextBox.Text ?? string.Empty;
        }

        var result = _templateService.ApplyTemplate(tmpl.Body, project);

        _templateState.Record(
            tmpl,
            baseDescription,
            result,
            TemplateHasDescriptionPlaceholder(tmpl));

        SetDescriptionText(result);
        StatusTextBlock.Text = LocalizationHelper.Format("Status.Template.Applied", tmpl.Name);

        _logger.Info(
            LocalizationHelper.Format("Log.Template.Applied", tmpl.Name),
            TemplateLogSource);
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

        await UploadDraftAsync(_activeDraft);
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
            StatusTextBlock.Text = "Bitte zuerst ein Video auswaehlen.";
            return;
        }

        if (_activeDraft != draft)
        {
            SetActiveDraft(draft);
        }

        var project = BuildUploadProjectFromUi(includeScheduling: true, draftOverride: draft);

        if (!string.IsNullOrWhiteSpace(project.ThumbnailPath) &&
            !File.Exists(project.ThumbnailPath))
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Status.Upload.ThumbnailNotFound");
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
                StatusTextBlock.Text = LocalizationHelper.Get("Status.Upload.ThumbnailTooLarge");
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
            UpdateUploadStatus(draft, UploadDraftUploadState.Failed, validationText);
            _logger.Warning(
                LocalizationHelper.Format("Log.Upload.ValidationFailed", ex.Message),
                UploadLogSource);
            ScheduleDraftPersistence();
            return;
        }

        if (project.Platform == PlatformType.YouTube && !_youTubeClient.IsConnected)
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Status.Upload.ConnectYouTube");
            return;
        }

        var uploadCts = new CancellationTokenSource();
        _activeUploadCts = uploadCts;
        _activeUploadDraft = draft;
        _isUploading = true;
        UpdateUploadButtonState();
        var cancellationToken = uploadCts.Token;

        var preparingText = LocalizationHelper.Get("Status.Upload.Preparing");
        UpdateUploadStatus(
            draft,
            UploadDraftUploadState.Uploading,
            preparingText,
            isProgressIndeterminate: true,
            progressPercent: 0);
        ScheduleDraftPersistence();

        _logger.Info(
            LocalizationHelper.Format("Log.Upload.Started", project.Title),
            UploadLogSource);
        ShowUploadProgress(preparingText);

        var progressReporter = new Progress<UploadProgressInfo>(info =>
        {
            ReportUploadProgress(info);
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

        try
        {
            Template? selectedTemplate = UploadView.TemplateComboBox.SelectedItem as Template;
            cancellationToken.ThrowIfCancellationRequested();

            var result = await _uploadService.UploadAsync(
                project,
                selectedTemplate,
                progressReporter,
                cancellationToken);

            await Task.Run(() => _uploadHistoryService.AddEntry(project, result));
            await LoadUploadHistoryAsync();

            if (result.Success)
            {
                var videoUrlText = result.VideoUrl?.ToString() ?? string.Empty;
                var successText = LocalizationHelper.Format("Status.Upload.Success", videoUrlText);
                UpdateUploadStatus(draft, UploadDraftUploadState.Completed, successText);
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

                TryAutoRemoveDraft(draft);
                ScheduleDraftPersistence();
            }
            else
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    var canceledText = LocalizationHelper.Get("Status.Upload.Canceled");
                    UpdateUploadStatus(
                        draft,
                        UploadDraftUploadState.Pending,
                        canceledText,
                        isProgressIndeterminate: false,
                        progressPercent: 0);
                    _logger.Info("Upload canceled.", UploadLogSource);
                    ScheduleDraftPersistence();
                    return;
                }

                var errorText = result.ErrorMessage ?? string.Empty;
                var failure = LocalizationHelper.Format("Status.Upload.Failed", errorText);
                UpdateUploadStatus(draft, UploadDraftUploadState.Failed, failure);
                _logger.Error(
                    LocalizationHelper.Format("Log.Upload.Failed", errorText),
                    UploadLogSource);
                ScheduleDraftPersistence();
            }
        }
        catch (OperationCanceledException)
        {
            var canceledText = LocalizationHelper.Get("Status.Upload.Canceled");
            UpdateUploadStatus(
                draft,
                UploadDraftUploadState.Pending,
                canceledText,
                isProgressIndeterminate: false,
                progressPercent: 0);
            _logger.Info("Upload canceled.", UploadLogSource);
            ScheduleDraftPersistence();
        }
        catch (Exception ex)
        {
            var unexpected = LocalizationHelper.Format("Status.Upload.UnexpectedError", ex.Message);
            UpdateUploadStatus(draft, UploadDraftUploadState.Failed, unexpected);
            _logger.Error(
                LocalizationHelper.Format("Log.Upload.UnexpectedError", ex.Message),
                UploadLogSource,
                ex);
            ScheduleDraftPersistence();
        }
        finally
        {
            if (ReferenceEquals(_activeUploadCts, uploadCts))
            {
                _activeUploadCts = null;
            }

            uploadCts.Dispose();

            if (_activeUploadDraft == draft)
            {
                _activeUploadDraft = null;
            }

            _isUploading = false;
            HideUploadProgress();
            UpdateUploadButtonState();
            UpdateLogLinkIndicator();
            ScheduleDraftPersistence();
        }
    }


    #endregion

    #region Upload Progress UI

    private void ShowUploadProgress(string message)
    {
        OnUiThread(() =>
        {
            UploadProgressBar.Visibility = Visibility.Visible;
            UploadProgressLabel.Visibility = Visibility.Visible;

            UploadProgressBar.IsIndeterminate = true;
            UploadProgressBar.Value = 0;
            UploadProgressLabel.Text = message;
        });
    }

    private void ReportUploadProgress(UploadProgressInfo info)
    {
        OnUiThread(() =>
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
        });
    }

    private void HideUploadProgress()
    {
        OnUiThread(() =>
        {
            UploadProgressBar.Visibility = Visibility.Collapsed;
            UploadProgressLabel.Visibility = Visibility.Collapsed;
            UploadProgressBar.IsIndeterminate = false;
            UploadProgressBar.Value = 0;
            UploadProgressLabel.Text = string.Empty;
        });
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

    private UploadProject BuildUploadProjectFromUi(bool includeScheduling, UploadDraft? draftOverride = null)
    {
        var platform = PlatformType.YouTube;
        if (UploadView.PlatformYouTubeToggle.IsChecked == true)
        {
            platform = PlatformType.YouTube;
        }

        var visibility = _settings.DefaultVisibility;
        if (UploadView.VisibilityComboBox.SelectedItem is ComboBoxItem visItem && visItem.Tag is VideoVisibility visEnum)
        {
            visibility = visEnum;
        }

        string? playlistId;
        string? playlistTitle = null;
        if (UploadView.PlaylistComboBox.SelectedItem is YouTubePlaylistInfo plItem)
        {
            playlistId = plItem.Id;
            playlistTitle = plItem.Title;
        }
        else
        {
            playlistId = _settings.DefaultPlaylistId;
        }

        DateTimeOffset? scheduledTime = null;
        if (includeScheduling && UploadView.ScheduleCheckBox.IsChecked == true)
        {
            if (UploadView.ScheduleDatePicker.SelectedDate is DateTime date)
            {
                var timeText = UploadView.ScheduleTimeTextBox.Text;
                if (!TimeSpan.TryParse(timeText, CultureInfo.CurrentCulture, out var timeOfDay))
                {
                    timeOfDay = TimeSpan.Parse(Constants.DefaultSchedulingTime);
                }

                var localDateTime = date.Date + timeOfDay;
                scheduledTime = new DateTimeOffset(localDateTime, DateTimeOffset.Now.Offset);
            }
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
            TranscriptText = draftOverride?.Transcript ?? UploadView.TranscriptTextBox.Text
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

    private static bool TemplateHasDescriptionPlaceholder(Template tmpl)
    {
        if (tmpl is null || string.IsNullOrWhiteSpace(tmpl.Body))
        {
            return false;
        }

        return Regex.IsMatch(tmpl.Body, @"\{+\s*description\s*\}+", RegexOptions.IgnoreCase);
    }

    private void ApplyGeneratedDescription(string newDescription)
    {
        if (_templateState.Template is not null && _templateState.HasDescriptionPlaceholder)
        {
            var project = BuildUploadProjectFromUi(includeScheduling: true);
            project.Description = newDescription ?? string.Empty;
            var applied = _templateService.ApplyTemplate(_templateState.Template.Body, project);
            _templateState.UpdateLastResult(applied);
            SetDescriptionText(applied);
        }
        else
        {
            AppendDescriptionText(newDescription);
        }
    }

    #endregion
}
