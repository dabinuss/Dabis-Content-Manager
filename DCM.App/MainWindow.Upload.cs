using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text.RegularExpressions;
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

    #region Video Drop Zone

    private readonly string[] _allowedVideoExtensions = { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm" };

    private void VideoDrop_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files?.Length == 1)
            {
                var ext = Path.GetExtension(files[0]).ToLowerInvariant();
                if (_allowedVideoExtensions.Contains(ext))
                {
                    e.Effects = DragDropEffects.Copy;
                    UploadView.VideoDropZone.BorderBrush = (SolidColorBrush)FindResource("PrimaryBrush");
                }
            }
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
            if (files?.Length == 1)
            {
                var ext = Path.GetExtension(files[0]).ToLowerInvariant();
                if (_allowedVideoExtensions.Contains(ext))
                {
                    SetVideoFile(files[0]);
                }
            }
        }
    }

    private void VideoDropZone_Click(object sender, MouseButtonEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = LocalizationHelper.Get("Dialog.Video.Open.Filter"),
            Title = LocalizationHelper.Get("Dialog.Video.Open.Title")
        };

        if (!string.IsNullOrEmpty(_settings?.DefaultVideoFolder) &&
            Directory.Exists(_settings.DefaultVideoFolder))
        {
            dialog.InitialDirectory = _settings.DefaultVideoFolder;
        }

        if (dialog.ShowDialog() == true)
        {
            SetVideoFile(dialog.FileName);
        }
    }

    private void SetVideoFile(string filePath)
    {
        UploadView.VideoPathTextBox.Text = filePath;
        var fileInfo = new FileInfo(filePath);

        var directory = fileInfo.DirectoryName;
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _settings.LastVideoFolder = directory;
            SaveSettings();
        }

        UploadView.VideoDropEmptyState.Visibility = Visibility.Collapsed;
        UploadView.VideoDropSelectedState.Visibility = Visibility.Visible;
        UpdateUploadButtonState();
        UpdateTranscriptionButtonState();

        _logger.Info(
            LocalizationHelper.Format("Log.Upload.VideoSelected", fileInfo.Name),
            UploadLogSource);

        _ = LoadVideoFileInfoAsync(filePath);
    }

    private async Task LoadVideoFileInfoAsync(string filePath)
    {
        try
        {
            var (fileName, fileSize) = await Task.Run(() =>
            {
                var fileInfo = new FileInfo(filePath);
                return (fileInfo.Name, fileInfo.Length);
            });

            if (!Dispatcher.CheckAccess())
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    UploadView.VideoFileNameTextBlock.Text = fileName;
                    UploadView.VideoFileSizeTextBlock.Text = FormatFileSize(fileSize);
                });
            }
            else
            {
                UploadView.VideoFileNameTextBlock.Text = fileName;
                UploadView.VideoFileSizeTextBlock.Text = FormatFileSize(fileSize);
            }

            _logger.Info(
                LocalizationHelper.Format("Log.Upload.VideoSelected", fileName),
                UploadLogSource);

            _ = TryAutoTranscribeAsync(filePath);
        }
        catch (Exception ex)
        {
            _logger.Error(
                LocalizationHelper.Format("Log.Upload.VideoInfoError", ex.Message),
                UploadLogSource,
                ex);

            await Dispatcher.InvokeAsync(() =>
            {
                UploadView.VideoFileNameTextBlock.Text = Path.GetFileName(filePath);
                UploadView.VideoFileSizeTextBlock.Text = LocalizationHelper.Get("Upload.VideoFileSize.Unknown");
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

    private void TitleTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateUploadButtonState();
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
            _lastAppliedTemplate = null;
            _lastAppliedTemplateHasDescriptionPlaceholder = false;
            _lastAppliedTemplateBaseDescription = null;
            _lastAppliedTemplateResult = null;
            return;
        }

        if (_lastAppliedTemplate is not null)
        {
            var trimmedCurrent = current.Trim();
            if (!string.Equals(trimmedCurrent, _lastAppliedTemplateResult?.Trim(), StringComparison.Ordinal))
            {
                _lastAppliedTemplateBaseDescription = current;
            }
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
        var hasVideo = !string.IsNullOrWhiteSpace(UploadView.VideoPathTextBox.Text);
        var hasTitle = !string.IsNullOrWhiteSpace(UploadView.TitleTextBox.Text);

        UploadView.UploadButton.IsEnabled = hasVideo && hasTitle;
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

        var fileInfo = new FileInfo(filePath);
        UploadView.ThumbnailFileNameTextBlock.Text = fileInfo.Name;

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

            _logger.Info(
                LocalizationHelper.Format("Log.Upload.ThumbnailSelected", fileInfo.Name),
                UploadLogSource);
        }
        catch (Exception ex)
        {
            _logger.Error(
                LocalizationHelper.Format("Log.Upload.ThumbnailLoadError", ex.Message),
                UploadLogSource,
                ex);
        }
    }

    private void ThumbnailClearButton_Click(object sender, RoutedEventArgs e)
    {
        UploadView.ThumbnailPathTextBox.Text = string.Empty;
        UploadView.ThumbnailPreviewImage.Source = null;

        UploadView.ThumbnailEmptyState.Visibility = Visibility.Visible;
        UploadView.ThumbnailPreviewState.Visibility = Visibility.Collapsed;
    }

    private void VideoChangeButton_Click(object sender, RoutedEventArgs e)
    {
        VideoDropZone_Click(sender, null!);
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

        var project = BuildUploadProjectFromUi(includeScheduling: false);

        // Prevent compounding description when re-applying the same template with {DESCRIPTION}
        if (_lastAppliedTemplate is not null &&
            tmpl.Id == _lastAppliedTemplate.Id &&
            _lastAppliedTemplateBaseDescription is not null)
        {
            project.Description = _lastAppliedTemplateBaseDescription;
        }
        else
        {
            _lastAppliedTemplateBaseDescription = UploadView.DescriptionTextBox.Text ?? string.Empty;
        }

        var result = _templateService.ApplyTemplate(tmpl.Body, project);

        _lastAppliedTemplate = tmpl;
        _lastAppliedTemplateHasDescriptionPlaceholder = TemplateHasDescriptionPlaceholder(tmpl);
        _lastAppliedTemplateResult = result;

        SetDescriptionText(result);
        StatusTextBlock.Text = LocalizationHelper.Format("Status.Template.Applied", tmpl.Name);

        _logger.Info(
            LocalizationHelper.Format("Log.Template.Applied", tmpl.Name),
            TemplateLogSource);
    }

    private async void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.ConfirmBeforeUpload)
        {
            var confirmResult = MessageBox.Show(
                this,
                LocalizationHelper.Get("Dialog.Upload.Confirm.Text"),
                LocalizationHelper.Get("Dialog.Upload.Confirm.Title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmResult != MessageBoxResult.Yes)
            {
                StatusTextBlock.Text = LocalizationHelper.Get("Status.Upload.Canceled");
                return;
            }
        }

        var project = BuildUploadProjectFromUi(includeScheduling: true);

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
            StatusTextBlock.Text = LocalizationHelper.Format("Status.Upload.ValidationFailed", ex.Message);
            _logger.Warning(
                LocalizationHelper.Format("Log.Upload.ValidationFailed", ex.Message),
                UploadLogSource);
            return;
        }

        if (project.Platform == PlatformType.YouTube && !_youTubeClient.IsConnected)
        {
            StatusTextBlock.Text = LocalizationHelper.Get("Status.Upload.ConnectYouTube");
            return;
        }

        _logger.Info(
            LocalizationHelper.Format("Log.Upload.Started", project.Title),
            UploadLogSource);
        var preparingText = LocalizationHelper.Get("Status.Upload.Preparing");
        StatusTextBlock.Text = preparingText;
        ShowUploadProgress(preparingText);

        var progressReporter = new Progress<UploadProgressInfo>(ReportUploadProgress);

        try
        {
            Template? selectedTemplate = UploadView.TemplateComboBox.SelectedItem as Template;

            var result = await _uploadService.UploadAsync(
                project,
                selectedTemplate,
                progressReporter,
                CancellationToken.None);

            await Task.Run(() => _uploadHistoryService.AddEntry(project, result));
            await LoadUploadHistoryAsync();

            if (result.Success)
            {
                var videoUrlText = result.VideoUrl?.ToString() ?? string.Empty;
                StatusTextBlock.Text = LocalizationHelper.Format("Status.Upload.Success", videoUrlText);
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
                        // Browser öffnen ist Komfort
                    }
                }
            }
            else
            {
                var errorText = result.ErrorMessage ?? string.Empty;
                StatusTextBlock.Text = LocalizationHelper.Format("Status.Upload.Failed", errorText);
                _logger.Error(
                    LocalizationHelper.Format("Log.Upload.Failed", errorText),
                    UploadLogSource);
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = LocalizationHelper.Format("Status.Upload.UnexpectedError", ex.Message);
            _logger.Error(
                LocalizationHelper.Format("Log.Upload.UnexpectedError", ex.Message),
                UploadLogSource,
                ex);
        }
        finally
        {
            HideUploadProgress();
            UpdateLogLinkIndicator();
        }
    }

    #endregion

    #region Upload Progress UI

    private void ShowUploadProgress(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ShowUploadProgress(message));
            return;
        }

        UploadProgressBar.Visibility = Visibility.Visible;
        UploadProgressLabel.Visibility = Visibility.Visible;

        UploadProgressBar.IsIndeterminate = true;
        UploadProgressBar.Value = 0;
        UploadProgressLabel.Text = message;
    }

    private void ReportUploadProgress(UploadProgressInfo info)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ReportUploadProgress(info));
            return;
        }

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
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(HideUploadProgress);
            return;
        }

        UploadProgressBar.Visibility = Visibility.Collapsed;
        UploadProgressLabel.Visibility = Visibility.Collapsed;
        UploadProgressBar.IsIndeterminate = false;
        UploadProgressBar.Value = 0;
        UploadProgressLabel.Text = string.Empty;
    }

    #endregion

    #region Upload Helpers

    private UploadProject BuildUploadProjectFromUi(bool includeScheduling)
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
        if (UploadView.PlaylistComboBox.SelectedItem is YouTubePlaylistInfo plItem)
        {
            playlistId = plItem.Id;
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
            VideoFilePath = UploadView.VideoPathTextBox.Text ?? string.Empty,
            Title = UploadView.TitleTextBox.Text ?? string.Empty,
            Description = UploadView.DescriptionTextBox.Text ?? string.Empty,
            Platform = platform,
            Visibility = visibility,
            PlaylistId = playlistId,
            ScheduledTime = scheduledTime,
            ThumbnailPath = UploadView.ThumbnailPathTextBox.Text,
            TranscriptText = UploadView.TranscriptTextBox.Text
        };

        project.SetTagsFromCsv(UploadView.TagsTextBox.Text ?? string.Empty);

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
        if (_lastAppliedTemplate is not null && _lastAppliedTemplateHasDescriptionPlaceholder)
        {
            var project = BuildUploadProjectFromUi(includeScheduling: false);
            project.Description = newDescription ?? string.Empty;
            var applied = _templateService.ApplyTemplate(_lastAppliedTemplate.Body, project);
            _lastAppliedTemplateResult = applied;
            _lastAppliedTemplateBaseDescription = newDescription;
            SetDescriptionText(applied);
        }
        else
        {
            AppendDescriptionText(newDescription);
        }
    }

    #endregion
}
