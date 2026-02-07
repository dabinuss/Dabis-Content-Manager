using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DCM.App.Models;
using DCM.Core;
using DCM.Core.Models;

namespace DCM.App.Views;

public partial class ClipperView : UserControl
{
    private readonly DispatcherTimer _previewTimer;
    private ClipCandidate? _currentPreviewCandidate;
    private string? _currentVideoPath;
    private string? _loadedVideoPath;
    private bool _isPlaying;
    private bool _isMediaReady;
    private bool _isDraggingSlider;
    private bool _isApplyingSettings;
    private bool _isDraggingSubtitle;
    private Point _subtitleDragStart;
    private double _subtitleStartLeft;
    private double _subtitleStartTop;
    private bool _seekPendingAfterMediaOpen;
    private bool _restartFromClipStartOnNextPlay;
    private int _seekOperationVersion;
    private int _previewFrameRequestVersion;
    private int _previewPrefetchVersion;
    private string? _ffmpegPath;
    private readonly Dictionary<string, BitmapSource> _previewFrameCache = new(StringComparer.OrdinalIgnoreCase);
    private List<ClipCandidate> _currentCandidates = new();

    public ClipperView()
    {
        InitializeComponent();

        _previewTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _previewTimer.Tick += PreviewTimer_Tick;

        CandidatesListBox.SelectionChanged += CandidatesListBox_SelectionChanged;

        // Standard-Lautstärke
        PreviewMediaElement.Volume = 0.5;

        CropModeComboBox.SelectedIndex = 0;
        EnableSubtitlesCheckBox.IsChecked = true;
        WordHighlightCheckBox.IsChecked = true;

        SubtitleFontSizeSlider.ValueChanged += SubtitleSettingsControl_ValueChanged;
        SubtitlePositionXSlider.ValueChanged += SubtitleSettingsControl_ValueChanged;
        SubtitlePositionYSlider.ValueChanged += SubtitleSettingsControl_ValueChanged;
        ManualCropOffsetSlider.ValueChanged += ManualCropOffsetSlider_ValueChanged;
        EnableSubtitlesCheckBox.Checked += SubtitleSettingsControl_Changed;
        EnableSubtitlesCheckBox.Unchecked += SubtitleSettingsControl_Changed;
        WordHighlightCheckBox.Checked += SubtitleSettingsControl_Changed;
        WordHighlightCheckBox.Unchecked += SubtitleSettingsControl_Changed;

        Loaded += ClipperView_Loaded;
    }

    public event SelectionChangedEventHandler? DraftSelectionChanged;
    public event EventHandler? SettingsChanged;

    public UploadDraft? SelectedDraft => (DraftListBox.SelectedItem as ClipperDraftItem)?.Draft;

    public void ApplyClipperSettings(ClipperSettings settings)
    {
        if (settings is null)
        {
            return;
        }

        _isApplyingSettings = true;
        try
        {
            SelectCropMode(settings.DefaultCropMode);
            ManualCropOffsetSlider.Value = Math.Clamp(settings.ManualCropOffsetX, -1, 1);
            EnableSubtitlesCheckBox.IsChecked = settings.EnableSubtitlesByDefault;

            var subtitle = settings.SubtitleSettings ?? new ClipSubtitleSettings();
            WordHighlightCheckBox.IsChecked = subtitle.WordByWordHighlight;
            SubtitleFontSizeSlider.Value = Math.Clamp(subtitle.FontSize, SubtitleFontSizeSlider.Minimum, SubtitleFontSizeSlider.Maximum);
            SubtitlePositionXSlider.Value = Math.Clamp(subtitle.PositionX, 0, 1);
            SubtitlePositionYSlider.Value = Math.Clamp(subtitle.PositionY, 0, 1);
        }
        finally
        {
            _isApplyingSettings = false;
            UpdateManualCropUi();
            UpdateSubtitlePlacementPreview();
            UpdateSettingsValueLabels();
        }
    }

    public void ApplyToClipperSettings(ClipperSettings settings)
    {
        if (settings is null)
        {
            return;
        }

        settings.DefaultCropMode = GetSelectedCropMode();
        settings.ManualCropOffsetX = ManualCropOffsetSlider.Value;
        settings.EnableSubtitlesByDefault = EnableSubtitlesCheckBox.IsChecked == true;
        settings.SubtitleSettings ??= new ClipSubtitleSettings();
        settings.SubtitleSettings.WordByWordHighlight = WordHighlightCheckBox.IsChecked == true;
        settings.SubtitleSettings.FontSize = (int)Math.Round(SubtitleFontSizeSlider.Value);
        settings.SubtitleSettings.PositionX = SubtitlePositionXSlider.Value;
        settings.SubtitleSettings.PositionY = SubtitlePositionYSlider.Value;
    }

    public CropMode GetSelectedCropMode()
    {
        return CropModeComboBox.SelectedItem is ComboBoxItem item
            && item.Tag is string tag
            && Enum.TryParse<CropMode>(tag, out var mode)
            ? mode
            : CropMode.AutoDetect;
    }

    public double GetManualCropOffsetX() => ManualCropOffsetSlider.Value;

    public IReadOnlyList<ClipCandidate> SelectedCandidates =>
        CandidatesListBox.ItemsSource is IEnumerable<ClipCandidate> candidates
            ? candidates.Where(c => c.IsSelected).ToList()
            : Array.Empty<ClipCandidate>();

    public void SetDrafts(IEnumerable<ClipperDraftItem> drafts)
    {
        DraftListBox.ItemsSource = drafts?.ToList() ?? new List<ClipperDraftItem>();
    }

    public void SetCandidates(IEnumerable<ClipCandidate> candidates)
    {
        var list = candidates?.ToList() ?? new List<ClipCandidate>();
        _currentCandidates = list;
        CandidatesListBox.ItemsSource = list;

        if (list.Count > 0)
        {
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            CandidatesListBox.Visibility = Visibility.Visible;
            LoadingPanel.Visibility = Visibility.Collapsed;
            StartPreviewFramePrefetch();
        }
        else
        {
            EmptyStatePanel.Visibility = Visibility.Visible;
            CandidatesListBox.Visibility = Visibility.Collapsed;
            LoadingPanel.Visibility = Visibility.Collapsed;
            StopPreview();
        }
    }

    public void ShowLoading(string? statusText = null)
    {
        EmptyStatePanel.Visibility = Visibility.Collapsed;
        CandidatesListBox.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Visible;

        if (!string.IsNullOrEmpty(statusText))
        {
            LoadingStatusText.Text = statusText;
        }

        StopPreview();
    }

    public void ShowEmptyState()
    {
        EmptyStatePanel.Visibility = Visibility.Visible;
        CandidatesListBox.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Collapsed;
        StopPreview();
    }

    /// <summary>
    /// Setzt den Video-Pfad für die Preview.
    /// </summary>
    public void SetVideoPath(string? videoPath)
    {
        _currentVideoPath = videoPath;

        if (string.IsNullOrWhiteSpace(videoPath))
        {
            StopPreview();
            ShowVideoEmptyState();
        }
        else if (!string.Equals(_loadedVideoPath, videoPath, StringComparison.OrdinalIgnoreCase))
        {
            // Neues Video - muss neu geladen werden
            _loadedVideoPath = null;
            _isMediaReady = false;
            StartPreviewFramePrefetch();
        }
    }

    private void DraftListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DraftSelectionChanged?.Invoke(sender, e);
        StopPreview();
        ShowVideoEmptyState();
    }

    private void CandidatesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CandidatesListBox.SelectedItem is ClipCandidate candidate)
        {
            LoadCandidatePreview(candidate);
        }
    }

    private void LoadCandidatePreview(ClipCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(_currentVideoPath))
        {
            return;
        }

        // Stoppe aktuelle Wiedergabe
        _isPlaying = false;
        _previewTimer.Stop();
        UpdatePlayButtonIcon(false);

        _currentPreviewCandidate = candidate;
        _restartFromClipStartOnNextPlay = true;

        // UI aktualisieren
        PreviewEndTimeText.Text = candidate.DurationFormatted;
        PreviewCurrentTimeText.Text = "0:00";
        PreviewProgressSlider.Value = 0;

        // Clip-Details aktualisieren
        UpdateClipDetails(candidate);
        LoadClipStartFramePreviewAsync(_currentVideoPath, candidate.Start);

        VideoPlayerPanel.Visibility = Visibility.Visible;
        VideoEmptyStatePanel.Visibility = Visibility.Collapsed;

        try
        {
            // Prüfen ob das Video bereits geladen ist
            if (_isMediaReady && string.Equals(_loadedVideoPath, _currentVideoPath, StringComparison.OrdinalIgnoreCase))
            {
                // Video ist bereits geladen - nur Position ändern
                PreviewMediaElement.Pause();
                SeekToClipStart(renderPreviewFrame: true);
            }
            else
            {
                // Video neu laden
                _isMediaReady = false;
                _seekPendingAfterMediaOpen = true;
                PreviewMediaElement.Source = new Uri(_currentVideoPath);
                _loadedVideoPath = _currentVideoPath;
            }
        }
        catch
        {
            ShowVideoEmptyState();
        }
    }

    private void PreviewMediaElement_MediaOpened(object sender, RoutedEventArgs e)
    {
        _isMediaReady = true;

        if (_currentPreviewCandidate is null)
        {
            return;
        }

        if (_seekPendingAfterMediaOpen)
        {
            _seekPendingAfterMediaOpen = false;
            SeekToClipStart(renderPreviewFrame: true);
        }

        UpdateSubtitlePlacementPreview();
    }

    private void PreviewMediaElement_MediaEnded(object sender, RoutedEventArgs e)
    {
        StopPlayback();
    }

    private void PreviewPlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isPlaying)
        {
            PausePlayback();
        }
        else
        {
            StartPlayback();
        }
    }

    private void PreviewStopButton_Click(object sender, RoutedEventArgs e)
    {
        StopPlayback();
    }

    private void PreviewRestartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPreviewCandidate is null)
        {
            return;
        }

        _restartFromClipStartOnNextPlay = true;
        StartPlayback();
    }

    private void PreviewVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PreviewMediaElement is not null)
        {
            PreviewMediaElement.Volume = e.NewValue;
        }

        // Volume-Icon aktualisieren
        UpdateVolumeIcon(e.NewValue);
    }

    private void PreviewMuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (PreviewVolumeSlider.Value > 0)
        {
            // Mute
            PreviewVolumeSlider.Tag = PreviewVolumeSlider.Value; // Speichere alte Lautstärke
            PreviewVolumeSlider.Value = 0;
        }
        else
        {
            // Unmute
            var previousVolume = PreviewVolumeSlider.Tag as double? ?? 0.5;
            PreviewVolumeSlider.Value = previousVolume > 0 ? previousVolume : 0.5;
        }
    }

    private void UpdateVolumeIcon(double volume)
    {
        if (PreviewVolumeIcon is null)
        {
            return;
        }

        // volume_up = e050, volume_down = e04d, volume_mute = e04e, volume_off = e04f
        PreviewVolumeIcon.Text = volume switch
        {
            0 => "\ue04f",      // volume_off
            < 0.3 => "\ue04e",  // volume_mute
            < 0.7 => "\ue04d",  // volume_down
            _ => "\ue050"       // volume_up
        };
    }

    private void PreviewProgressSlider_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _isDraggingSlider = true;
    }

    private void PreviewProgressSlider_PreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _isDraggingSlider = false;
        SeekToSliderPosition();
    }

    private void PreviewProgressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isDraggingSlider && _currentPreviewCandidate is not null)
        {
            // Live-Update der Zeitanzeige während des Ziehens
            var clipDuration = _currentPreviewCandidate.Duration;
            var elapsed = TimeSpan.FromSeconds((e.NewValue / 100.0) * clipDuration.TotalSeconds);
            PreviewCurrentTimeText.Text = FormatTimeSpan(elapsed);
        }
    }

    private void SeekToSliderPosition()
    {
        if (_currentPreviewCandidate is null || !_isMediaReady)
        {
            return;
        }

        var clipStart = _currentPreviewCandidate.Start;
        var clipDuration = _currentPreviewCandidate.Duration;
        var progress = PreviewProgressSlider.Value / 100.0;
        var newPosition = clipStart + TimeSpan.FromSeconds(progress * clipDuration.TotalSeconds);

        PreviewMediaElement.Position = newPosition;
        _restartFromClipStartOnNextPlay = false;
    }

    private void UpdateClipDetails(ClipCandidate candidate)
    {
        ClipScoreText.Text = candidate.Score.ToString("F1");
        ClipTimeRangeText.Text = $"{candidate.StartFormatted} - {candidate.EndFormatted} ({candidate.DurationFormatted})";
        ClipReasonText.Text = candidate.Reason;
    }

    private void ClearClipDetails()
    {
        ClipScoreText.Text = "0.0";
        ClipTimeRangeText.Text = "0:00 - 0:00 (0s)";
        ClipReasonText.Text = string.Empty;
    }

    private void StartPlayback()
    {
        if (_currentPreviewCandidate is null || !_isMediaReady)
        {
            return;
        }

        // Nach Kandidatenwechsel erzwingt der erste Play-Start einen exakten Seek.
        if (_restartFromClipStartOnNextPlay || IsOutsideCurrentClip(PreviewMediaElement.Position, _currentPreviewCandidate))
        {
            SeekToClipStart(renderPreviewFrame: false);
            _restartFromClipStartOnNextPlay = false;
        }

        PreviewFrameImage.Visibility = Visibility.Collapsed;
        _isPlaying = true;
        PreviewMediaElement.Play();
        _previewTimer.Start();
        UpdatePlayButtonIcon(true);
    }

    private void PausePlayback()
    {
        _isPlaying = false;
        PreviewMediaElement.Pause();
        _previewTimer.Stop();
        if (PreviewFrameImage.Source is not null)
        {
            PreviewFrameImage.Visibility = Visibility.Visible;
        }
        UpdatePlayButtonIcon(false);
    }

    private void StopPlayback()
    {
        _seekOperationVersion++;
        _isPlaying = false;
        _previewTimer.Stop();
        PreviewMediaElement.Pause();

        if (_currentPreviewCandidate is not null && _isMediaReady)
        {
            SeekToClipStart(renderPreviewFrame: true);
        }

        PreviewCurrentTimeText.Text = "0:00";
        PreviewProgressSlider.Value = 0;
        if (PreviewFrameImage.Source is not null)
        {
            PreviewFrameImage.Visibility = Visibility.Visible;
        }
        UpdatePlayButtonIcon(false);
    }

    private void StopPreview()
    {
        _previewFrameRequestVersion++;
        _previewPrefetchVersion++;
        _seekOperationVersion++;
        _isPlaying = false;
        _isMediaReady = false;
        _previewTimer.Stop();
        _currentPreviewCandidate = null;
        _loadedVideoPath = null;
        _seekPendingAfterMediaOpen = false;
        _restartFromClipStartOnNextPlay = false;

        try
        {
            PreviewMediaElement.Stop();
            PreviewMediaElement.Source = null;
        }
        catch
        {
            // Ignorieren
        }

        PreviewFrameImage.Source = null;
        PreviewFrameImage.Visibility = Visibility.Collapsed;
    }

    private void ShowVideoEmptyState()
    {
        VideoPlayerPanel.Visibility = Visibility.Collapsed;
        VideoEmptyStatePanel.Visibility = Visibility.Visible;
        PreviewFrameImage.Visibility = Visibility.Collapsed;
    }

    private void ClipperView_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateManualCropUi();
        UpdateSubtitlePlacementPreview();
        UpdateSettingsValueLabels();
    }

    private void CropModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateManualCropUi();
        CommitClipperSettingsChange();
    }

    private void UpdateManualCropUi()
    {
        ManualCropOffsetPanel.Visibility = GetSelectedCropMode() == CropMode.Manual
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SubtitleSettingsControl_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        UpdateSettingsValueLabels();
        UpdateSubtitlePlacementPreview();
        CommitClipperSettingsChange();
    }

    private void SubtitleSettingsControl_Changed(object sender, RoutedEventArgs e)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        UpdateSubtitlePlacementPreview();
        CommitClipperSettingsChange();
    }

    private void ManualCropOffsetSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateSettingsValueLabels();
        CommitClipperSettingsChange();
    }

    private void SubtitleOverlayCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (EnableSubtitlesCheckBox.IsChecked != true)
        {
            return;
        }

        _isDraggingSubtitle = true;
        _subtitleDragStart = e.GetPosition(SubtitleOverlayCanvas);
        _subtitleStartLeft = Canvas.GetLeft(SubtitlePlacementPreview);
        _subtitleStartTop = Canvas.GetTop(SubtitlePlacementPreview);
        if (double.IsNaN(_subtitleStartLeft))
        {
            _subtitleStartLeft = 0;
        }
        if (double.IsNaN(_subtitleStartTop))
        {
            _subtitleStartTop = 0;
        }

        SubtitleOverlayCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void SubtitleOverlayCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingSubtitle || EnableSubtitlesCheckBox.IsChecked != true)
        {
            return;
        }

        var canvasWidth = SubtitleOverlayCanvas.ActualWidth;
        var canvasHeight = SubtitleOverlayCanvas.ActualHeight;
        if (canvasWidth <= 0 || canvasHeight <= 0)
        {
            return;
        }

        var previewSize = MeasureSubtitlePreview();
        var maxLeft = Math.Max(0, canvasWidth - previewSize.Width);
        var maxTop = Math.Max(0, canvasHeight - previewSize.Height);

        var current = e.GetPosition(SubtitleOverlayCanvas);
        var left = Math.Clamp(_subtitleStartLeft + (current.X - _subtitleDragStart.X), 0, maxLeft);
        var top = Math.Clamp(_subtitleStartTop + (current.Y - _subtitleDragStart.Y), 0, maxTop);

        _isApplyingSettings = true;
        try
        {
            Canvas.SetLeft(SubtitlePlacementPreview, left);
            Canvas.SetTop(SubtitlePlacementPreview, top);

            var centerX = (left + previewSize.Width / 2.0) / canvasWidth;
            var centerY = (top + previewSize.Height / 2.0) / canvasHeight;
            SubtitlePositionXSlider.Value = Math.Clamp(centerX, 0, 1);
            SubtitlePositionYSlider.Value = Math.Clamp(centerY, 0, 1);
        }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    private void SubtitleOverlayCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingSubtitle)
        {
            return;
        }

        _isDraggingSubtitle = false;
        SubtitleOverlayCanvas.ReleaseMouseCapture();
        UpdateSettingsValueLabels();
        CommitClipperSettingsChange();
        e.Handled = true;
    }

    private void UpdateSubtitlePlacementPreview()
    {
        if (!IsLoaded || SubtitleOverlayCanvas is null || SubtitlePlacementPreview is null)
        {
            return;
        }

        UpdateSettingsValueLabels();

        if (SubtitlePlacementPreview.Child is TextBlock previewText)
        {
            previewText.FontSize = Math.Max(12, SubtitleFontSizeSlider.Value * 0.45);
        }

        var isVisible = EnableSubtitlesCheckBox.IsChecked == true && VideoPlayerPanel.Visibility == Visibility.Visible;
        SubtitleOverlayCanvas.IsHitTestVisible = isVisible;
        SubtitlePlacementPreview.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;

        if (!isVisible)
        {
            return;
        }

        var canvasWidth = SubtitleOverlayCanvas.ActualWidth;
        var canvasHeight = SubtitleOverlayCanvas.ActualHeight;
        if (canvasWidth <= 0 || canvasHeight <= 0)
        {
            return;
        }

        var previewSize = MeasureSubtitlePreview();
        var maxLeft = Math.Max(0, canvasWidth - previewSize.Width);
        var maxTop = Math.Max(0, canvasHeight - previewSize.Height);

        var left = Math.Clamp((SubtitlePositionXSlider.Value * canvasWidth) - (previewSize.Width / 2.0), 0, maxLeft);
        var top = Math.Clamp((SubtitlePositionYSlider.Value * canvasHeight) - (previewSize.Height / 2.0), 0, maxTop);

        Canvas.SetLeft(SubtitlePlacementPreview, left);
        Canvas.SetTop(SubtitlePlacementPreview, top);
    }

    private void UpdateSettingsValueLabels()
    {
        SubtitleFontSizeValueText.Text = ((int)Math.Round(SubtitleFontSizeSlider.Value)).ToString(CultureInfo.CurrentCulture);
        SubtitlePositionXValueText.Text = SubtitlePositionXSlider.Value.ToString("0.00", CultureInfo.CurrentCulture);
        SubtitlePositionYValueText.Text = SubtitlePositionYSlider.Value.ToString("0.00", CultureInfo.CurrentCulture);
        ManualCropOffsetValueText.Text = ManualCropOffsetSlider.Value.ToString("0.00", CultureInfo.CurrentCulture);
    }

    private void SubtitlePresetTopButton_Click(object sender, RoutedEventArgs e) => ApplySubtitlePreset(0.5, 0.20);

    private void SubtitlePresetMiddleButton_Click(object sender, RoutedEventArgs e) => ApplySubtitlePreset(0.5, 0.50);

    private void SubtitlePresetBottomButton_Click(object sender, RoutedEventArgs e) => ApplySubtitlePreset(0.5, 0.78);

    private void SubtitlePresetResetButton_Click(object sender, RoutedEventArgs e) => ApplySubtitlePreset(0.5, 0.70);

    private void ApplySubtitlePreset(double x, double y)
    {
        _isApplyingSettings = true;
        try
        {
            SubtitlePositionXSlider.Value = Math.Clamp(x, 0, 1);
            SubtitlePositionYSlider.Value = Math.Clamp(y, 0, 1);
        }
        finally
        {
            _isApplyingSettings = false;
        }

        UpdateSettingsValueLabels();
        UpdateSubtitlePlacementPreview();
        CommitClipperSettingsChange();
    }

    private void CommitClipperSettingsChange()
    {
        if (_isApplyingSettings)
        {
            return;
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private Size MeasureSubtitlePreview()
    {
        SubtitlePlacementPreview.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var desired = SubtitlePlacementPreview.DesiredSize;
        return new Size(
            desired.Width <= 0 ? 40 : desired.Width,
            desired.Height <= 0 ? 20 : desired.Height);
    }

    private void SelectCropMode(CropMode mode)
    {
        foreach (var item in CropModeComboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string tag && Enum.TryParse<CropMode>(tag, out var parsed) && parsed == mode)
            {
                CropModeComboBox.SelectedItem = item;
                return;
            }
        }

        CropModeComboBox.SelectedIndex = 0;
    }

    private void PreviewTimer_Tick(object? sender, EventArgs e)
    {
        if (_currentPreviewCandidate is null || !_isPlaying || !_isMediaReady)
        {
            return;
        }

        // Slider nicht aktualisieren wenn der Benutzer gerade zieht
        if (_isDraggingSlider)
        {
            return;
        }

        var currentPosition = PreviewMediaElement.Position;
        var clipStart = _currentPreviewCandidate.Start;
        var clipEnd = _currentPreviewCandidate.End;
        var clipDuration = _currentPreviewCandidate.Duration;

        // Prüfen ob Endzeit erreicht
        if (currentPosition >= clipEnd)
        {
            StopPlayback();
            return;
        }

        // Fortschritt aktualisieren
        var elapsed = currentPosition - clipStart;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        var progress = clipDuration.TotalSeconds > 0
            ? (elapsed.TotalSeconds / clipDuration.TotalSeconds) * 100
            : 0;

        PreviewProgressSlider.Value = Math.Min(100, Math.Max(0, progress));
        PreviewCurrentTimeText.Text = FormatTimeSpan(elapsed);
    }

    private void UpdatePlayButtonIcon(bool isPlaying)
    {
        // Play = &#xe037; (play_arrow), Pause = &#xe034; (pause)
        PreviewPlayButtonIcon.Text = isPlaying ? "\ue034" : "\ue037";
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        }
        return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
    }

    private async void SeekToClipStart(bool renderPreviewFrame)
    {
        if (_currentPreviewCandidate is null || !_isMediaReady)
        {
            return;
        }

        var target = _currentPreviewCandidate.Start;
        var seekVersion = ++_seekOperationVersion;
        PreviewMediaElement.Position = target;

        if (!renderPreviewFrame)
        {
            return;
        }

        try
        {
            // Play/Pause mit kurzer Verzögerung rendert den gesuchten Frame zuverlässiger.
            PreviewMediaElement.Play();
            await Task.Delay(120);

            if (seekVersion != _seekOperationVersion || _isPlaying || _currentPreviewCandidate is null)
            {
                return;
            }

            PreviewMediaElement.Pause();
            PreviewMediaElement.Position = target;
            PreviewCurrentTimeText.Text = "0:00";
            PreviewProgressSlider.Value = 0;
        }
        catch
        {
            // ignore preview-only errors
        }
    }

    private static bool IsOutsideCurrentClip(TimeSpan position, ClipCandidate candidate)
    {
        return position < candidate.Start || position > candidate.End;
    }

    private async void LoadClipStartFramePreviewAsync(string? videoPath, TimeSpan clipStart)
    {
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
        {
            PreviewFrameImage.Source = null;
            PreviewFrameImage.Visibility = Visibility.Collapsed;
            return;
        }

        var cacheKey = BuildPreviewCacheKey(videoPath, clipStart);
        if (_previewFrameCache.TryGetValue(cacheKey, out var cached))
        {
            PreviewFrameImage.Source = cached;
            if (!_isPlaying)
            {
                PreviewFrameImage.Visibility = Visibility.Visible;
            }
            return;
        }

        var requestVersion = ++_previewFrameRequestVersion;
        var bitmap = await Task.Run(() => ExtractFrameBitmap(videoPath, clipStart));

        if (requestVersion != _previewFrameRequestVersion || bitmap is null)
        {
            return;
        }

        _previewFrameCache[cacheKey] = bitmap;
        PreviewFrameImage.Source = bitmap;
        if (!_isPlaying)
        {
            PreviewFrameImage.Visibility = Visibility.Visible;
        }
    }

    private BitmapImage? ExtractFrameBitmap(string videoPath, TimeSpan timestamp)
    {
        var ffmpegPath = FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            return null;
        }

        try
        {
            var seconds = timestamp.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture);
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-hide_banner -loglevel error -ss {seconds} -i \"{videoPath}\" -frames:v 1 -vf \"scale=640:-1:flags=fast_bilinear\" -f image2pipe -vcodec mjpeg pipe:1",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            using var stream = process.StandardOutput.BaseStream;
            using var imageStream = new MemoryStream();
            stream.CopyTo(imageStream);
            process.WaitForExit(5000);
            if (process.ExitCode != 0 || imageStream.Length == 0)
            {
                return null;
            }

            imageStream.Position = 0;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = imageStream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private string? FindFfmpeg()
    {
        if (!string.IsNullOrWhiteSpace(_ffmpegPath) && File.Exists(_ffmpegPath))
        {
            return _ffmpegPath;
        }

        var appFolder = Constants.FFmpegFolder;
        if (Directory.Exists(appFolder))
        {
            try
            {
                var found = Directory.EnumerateFiles(appFolder, "ffmpeg.exe", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(found))
                {
                    _ffmpegPath = found;
                    return _ffmpegPath;
                }
            }
            catch
            {
                // ignore
            }
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv))
        {
            return null;
        }

        foreach (var path in pathEnv.Split(';'))
        {
            try
            {
                var fullPath = Path.Combine(path.Trim(), "ffmpeg.exe");
                if (File.Exists(fullPath))
                {
                    _ffmpegPath = fullPath;
                    return _ffmpegPath;
                }
            }
            catch
            {
                // ignore
            }
        }

        return null;
    }

    private static string BuildPreviewCacheKey(string videoPath, TimeSpan timestamp)
    {
        var ms = (long)Math.Round(timestamp.TotalMilliseconds, MidpointRounding.AwayFromZero);
        return $"{videoPath}|{ms}";
    }

    private async void StartPreviewFramePrefetch()
    {
        if (string.IsNullOrWhiteSpace(_currentVideoPath) || _currentCandidates.Count == 0)
        {
            return;
        }

        var videoPath = _currentVideoPath;
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
        {
            return;
        }

        var version = ++_previewPrefetchVersion;

        // Prefetch die wichtigsten ersten Kandidaten, damit Auswahl sofort ein Bild hat.
        foreach (var candidate in _currentCandidates.Take(5))
        {
            if (version != _previewPrefetchVersion)
            {
                return;
            }

            var key = BuildPreviewCacheKey(videoPath, candidate.Start);
            if (_previewFrameCache.ContainsKey(key))
            {
                continue;
            }

            var bitmap = await Task.Run(() => ExtractFrameBitmap(videoPath, candidate.Start));
            if (bitmap is null || version != _previewPrefetchVersion)
            {
                continue;
            }

            _previewFrameCache[key] = bitmap;
        }
    }
}

/// <summary>
/// View-Model für Draft-Items in der Clipper-Liste.
/// </summary>
public sealed class ClipperDraftItem
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? VideoDuration { get; init; }
    public bool HasTranscript { get; init; }
    public UploadDraft? Draft { get; init; }
}
