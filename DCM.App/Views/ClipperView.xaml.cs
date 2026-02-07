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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DCM.App.Models;
using DCM.App.Services;
using DCM.Core;
using DCM.Core.Models;
using DCM.Core.Services;

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
    private Canvas? _activeSubtitleCanvas;
    private Border? _activeSubtitlePreview;
    private bool _seekPendingAfterMediaOpen;
    private bool _restartFromClipStartOnNextPlay;
    private int _seekOperationVersion;
    private int _previewFrameRequestVersion;
    private int _previewPrefetchVersion;
    private string? _ffmpegPath;
    private readonly Dictionary<string, BitmapSource> _previewFrameCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BitmapSource> _portraitPreviewCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CropRegionResult> _faceDetectionCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<SplitFaceAnchor>> _splitFaceAnchorCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _splitFaceAutoSelectionProcessedKeys = new(StringComparer.OrdinalIgnoreCase);
    private List<ClipCandidate> _currentCandidates = new();
    private readonly Dictionary<Guid, ClipperSettings> _candidateSettings = new();
    private ClipperSettings _defaultClipperSettings = new();
    private ClipCandidate? _editingCandidate;
    private string? _logoPath;
    private double _logoPositionX = 0.9;
    private double _logoPositionY = 0.05;
    private double _logoScale = 0.15;
    private bool _isDraggingLogo;
    private Point _logoDragStart;
    private Canvas? _activeLogoCanvas;
    private Image? _activeLogoImage;
    private int _portraitPreviewVersion;
    private readonly IFaceDetectionService _faceDetectionService = new FaceAiSharpDetectionService();
    private CancellationTokenSource? _faceDetectionCts;
    private CropRegionResult? _currentCropRegion;
    private BitmapSource? _splitSourceFrame;
    private NormalizedRect _splitPrimaryRegion = new() { X = 0, Y = 0, Width = 1, Height = 0.5 };
    private NormalizedRect _splitSecondaryRegion = new() { X = 0, Y = 0.5, Width = 1, Height = 0.5 };
    private bool _splitDuoMode = true;
    private SplitEditorInteractionMode _splitEditorInteractionMode = SplitEditorInteractionMode.Elements;
    private SplitDragMode _splitDragMode;
    private SplitResizeCorner _splitResizeCorner = SplitResizeCorner.None;
    private SplitRegionTarget _activeSplitRegion = SplitRegionTarget.Primary;
    private Point _splitDragStart;
    private NormalizedRect? _splitDragStartRect;
    private bool _isDraggingSplitDivider;
    private double _splitDividerRatio = 0.5;
    private const double MinSplitDividerRatio = 0.05;
    private const double MaxSplitDividerRatio = 0.95;

    // Kontinuierlicher FFmpeg-Prozess für Echtzeit-Preview
    private Process? _ffmpegPreviewProcess;
    private CancellationTokenSource? _ffmpegPreviewCts;
    private Task? _ffmpegFrameReaderTask;
    private readonly object _ffmpegLock = new();

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

        EnableSubtitlesCheckBox.IsChecked = true;
        WordHighlightCheckBox.IsChecked = true;

        SubtitleFontSizeSlider.ValueChanged += SubtitleSettingsControl_ValueChanged;
        SubtitlePositionXSlider.ValueChanged += SubtitleSettingsControl_ValueChanged;
        SubtitlePositionYSlider.ValueChanged += SubtitleSettingsControl_ValueChanged;
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

        _defaultClipperSettings = settings.DeepCopy();
        if (_editingCandidate is not null)
        {
            return;
        }

        ApplySettingsToControls(settings);
    }

    public void ApplyToClipperSettings(ClipperSettings settings)
    {
        if (settings is null)
        {
            return;
        }

        if (_editingCandidate is not null)
        {
            CopySettings(_defaultClipperSettings, settings);
            return;
        }

        settings.DefaultCropMode = CropMode.SplitLayout;
        settings.ManualCropOffsetX = 0;
        var splitLayout = BuildSplitLayoutFromEditor();
        splitLayout.Enabled = settings.DefaultCropMode == CropMode.SplitLayout;
        settings.DefaultSplitLayout = splitLayout;
        settings.EnableSubtitlesByDefault = EnableSubtitlesCheckBox.IsChecked == true;
        settings.DefaultLogoPath = _logoPath;
        settings.LogoPositionX = _logoPositionX;
        settings.LogoPositionY = _logoPositionY;
        settings.LogoScale = _logoScale;
        settings.SubtitleSettings ??= new ClipSubtitleSettings();
        settings.SubtitleSettings.WordByWordHighlight = WordHighlightCheckBox.IsChecked == true;
        settings.SubtitleSettings.FontSize = (int)Math.Round(SubtitleFontSizeSlider.Value);
        settings.SubtitleSettings.PositionX = SubtitlePositionXSlider.Value;
        settings.SubtitleSettings.PositionY = SubtitlePositionYSlider.Value;
        _defaultClipperSettings = settings.DeepCopy();
    }

    public ClipperSettings GetEffectiveSettingsForCandidate(ClipCandidate candidate, ClipperSettings defaultSettings)
    {
        if (candidate is null)
        {
            return (defaultSettings ?? new ClipperSettings()).DeepCopy();
        }

        if (_candidateSettings.TryGetValue(candidate.Id, out var candidateSettings))
        {
            return candidateSettings.DeepCopy();
        }

        return (_defaultClipperSettings ?? defaultSettings ?? new ClipperSettings()).DeepCopy();
    }

    private void ApplySettingsToControls(ClipperSettings settings)
    {
        _isApplyingSettings = true;
        try
        {
            var splitLayout = (settings.DefaultSplitLayout ?? new SplitLayoutConfig()).Clone();
            _splitPrimaryRegion = (splitLayout.PrimaryRegion ?? new NormalizedRect
            {
                X = 0,
                Y = 0,
                Width = 1,
                Height = 0.5
            }).Clone().ClampToCanvas(0.05, 1.0);
            _splitSecondaryRegion = (splitLayout.SecondaryRegion ?? new NormalizedRect
            {
                X = 0,
                Y = 0.5,
                Width = 1,
                Height = 0.5
            }).Clone().ClampToCanvas(0.05, 1.0);
            _splitDuoMode = splitLayout.Preset != SplitLayoutPreset.Solo;
            if (_splitDuoMode)
            {
                SyncSplitDividerRatioFromRegions();
                ApplyDividerRatioToSplitRegions();
            }
            else
            {
                _splitDividerRatio = 0.5;
            }
            EnableSubtitlesCheckBox.IsChecked = settings.EnableSubtitlesByDefault;

            var subtitle = settings.SubtitleSettings ?? new ClipSubtitleSettings();
            WordHighlightCheckBox.IsChecked = subtitle.WordByWordHighlight;
            SubtitleFontSizeSlider.Value = Math.Clamp(subtitle.FontSize, SubtitleFontSizeSlider.Minimum, SubtitleFontSizeSlider.Maximum);
            SubtitlePositionXSlider.Value = Math.Clamp(subtitle.PositionX, 0, 1);
            SubtitlePositionYSlider.Value = Math.Clamp(subtitle.PositionY, 0, 1);

            // Apply logo settings
            _logoPositionX = Math.Clamp(settings.LogoPositionX, 0, 1);
            _logoPositionY = Math.Clamp(settings.LogoPositionY, 0, 1);
            _logoScale = Math.Clamp(settings.LogoScale, 0.05, 0.4);
            LogoScaleSlider.Value = _logoScale;
            LogoScaleValueText.Text = $"{(int)(_logoScale * 100)}%";
            SetLogoPath(settings.DefaultLogoPath);
        }
        finally
        {
            _isApplyingSettings = false;
            UpdateManualCropUi();
            UpdateSubtitlePlacementPreview();
            UpdateSplitModeButtons();
            UpdateSplitSelectionOverlay();
            UpdateSplitPortraitPreview();
            UpdateSettingsValueLabels();
        }
    }

    public CropMode GetSelectedCropMode() => CropMode.SplitLayout;

    public double GetManualCropOffsetX() => 0;

    private SplitLayoutPreset GetSelectedSplitPreset()
    {
        return SplitLayoutPreset.TopBottom;
    }

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

        var validIds = list.Select(c => c.Id).ToHashSet();
        foreach (var staleId in _candidateSettings.Keys.Where(id => !validIds.Contains(id)).ToList())
        {
            _candidateSettings.Remove(staleId);
        }

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
            ExitCandidateEditor();
            StopPreview();
        }
    }

    public void ShowLoading(string? statusText = null)
    {
        ExitCandidateEditor();
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
        ExitCandidateEditor();
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
            ExitCandidateEditor();
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
        ExitCandidateEditor();
        DraftSelectionChanged?.Invoke(sender, e);
        StopPreview();
        ShowVideoEmptyState();
    }

    private void CandidatesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EditorPanel.Visibility == Visibility.Visible && CandidatesListBox.SelectedItem is ClipCandidate candidate)
        {
            LoadCandidatePreview(candidate);
        }
    }

    private void CandidateSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ClipCandidate candidate })
        {
            return;
        }

        _editingCandidate = candidate;
        CandidatesListBox.SelectedItem = candidate;

        EditorCandidateTitleText.Text = candidate.PreviewText;
        EditorCandidateTimeText.Text = $"{candidate.StartFormatted} - {candidate.EndFormatted} ({candidate.DurationFormatted})";

        if (_candidateSettings.TryGetValue(candidate.Id, out var candidateSettings))
        {
            ApplySettingsToControls(candidateSettings);
        }
        else
        {
            ApplySettingsToControls(_defaultClipperSettings);
        }

        EnterCandidateEditor();
        LoadCandidatePreview(candidate);
    }

    private void BackToOverviewButton_Click(object sender, RoutedEventArgs e)
    {
        ExitCandidateEditor();
    }

    private void EnterCandidateEditor()
    {
        OverviewPanel.Visibility = Visibility.Collapsed;
        EditorGridSplitter.Visibility = Visibility.Collapsed;
        EditorPanel.Visibility = Visibility.Visible;
    }

    private void ExitCandidateEditor()
    {
        SaveActiveCandidateSettings();
        _editingCandidate = null;
        ApplySettingsToControls(_defaultClipperSettings);
        OverviewPanel.Visibility = Visibility.Visible;
        EditorPanel.Visibility = Visibility.Collapsed;
        EditorGridSplitter.Visibility = Visibility.Collapsed;
        EditorSplitterColumn.Width = new GridLength(0);
        EditorColumn.MinWidth = 0;
        EditorColumn.Width = new GridLength(0);
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
        if (_currentPreviewCandidate is null || !_isMediaReady || string.IsNullOrWhiteSpace(_currentVideoPath))
        {
            return;
        }

        // Nach Kandidatenwechsel erzwingt der erste Play-Start einen exakten Seek.
        if (_restartFromClipStartOnNextPlay || IsOutsideCurrentClip(PreviewMediaElement.Position, _currentPreviewCandidate))
        {
            SeekToClipStart(renderPreviewFrame: false);
            _restartFromClipStartOnNextPlay = false;
        }

        // Starte kontinuierlichen FFmpeg-Preview-Prozess
        StartFfmpegPreviewProcess(_currentVideoPath, _currentPreviewCandidate.Start, _currentPreviewCandidate.End);

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
        StopFfmpegPreviewProcess();
        SplitPlaybackImage.Source = null;
        SplitPlaybackImage.Visibility = Visibility.Collapsed;

        UpdatePlayButtonIcon(false);
    }

    private void StopPlayback()
    {
        _seekOperationVersion++;
        _isPlaying = false;
        _previewTimer.Stop();
        PreviewMediaElement.Pause();
        StopFfmpegPreviewProcess();
        SplitPlaybackImage.Source = null;
        SplitPlaybackImage.Visibility = Visibility.Collapsed;

        if (_currentPreviewCandidate is not null && _isMediaReady)
        {
            SeekToClipStart(renderPreviewFrame: true);
        }

        PreviewCurrentTimeText.Text = "0:00";
        PreviewProgressSlider.Value = 0;
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
        StopFfmpegPreviewProcess();
        _currentPreviewCandidate = null;
        _currentCropRegion = null;
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

        CroppedPreviewImage.Source = null;
        SplitSourceImage.Source = null;
        SplitPortraitSingleImage.Source = null;
        SplitPortraitTopImage.Source = null;
        SplitPortraitBottomImage.Source = null;
        SplitPlaybackImage.Source = null;
        SplitPlaybackImage.Visibility = Visibility.Collapsed;
        _splitSourceFrame = null;
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
        ExitCandidateEditor();
        UpdateManualCropUi();
        UpdateSplitModeButtons();
        UpdateSplitSelectionOverlay();
        UpdateSplitPortraitPreview();
        UpdateSubtitlePlacementPreview();
        UpdateSettingsValueLabels();
    }

    private void RefreshPortraitPreview()
    {
        if (_currentPreviewCandidate is null || string.IsNullOrWhiteSpace(_currentVideoPath))
        {
            return;
        }

        if (GetSelectedCropMode() == CropMode.SplitLayout)
        {
            // Split-Preview rendert direkt aus dem geladenen Landscape-Frame;
            // ein asynchrones Nachladen erzeugt sonst sichtbares "Springen".
            UpdateSplitPortraitPreview();
            return;
        }

        // Portrait-Preview mit neuen Crop-Einstellungen neu laden
        LoadClipStartFramePreviewAsync(_currentVideoPath, _currentPreviewCandidate.Start);
    }

    private void UpdateManualCropUi()
    {
        UpdatePreviewModePanels();
        UpdateSplitModeButtons();
    }

    private void UpdatePreviewModePanels()
    {
        var isSplit = GetSelectedCropMode() == CropMode.SplitLayout;
        SplitWorkbenchPanel.Visibility = isSplit ? Visibility.Visible : Visibility.Collapsed;
        StandardPreviewPanel.Visibility = isSplit ? Visibility.Collapsed : Visibility.Visible;
        UpdateSubtitlePlacementPreview();
        UpdateLogoPosition();
    }

    private void SplitSoloButton_Click(object sender, RoutedEventArgs e)
    {
        _isDraggingSplitDivider = false;
        SplitPortraitDividerCanvas.ReleaseMouseCapture();
        _splitDuoMode = false;
        UpdateSplitModeButtons();
        UpdateSplitSelectionOverlay();
        UpdateSplitPortraitPreview();
        CommitClipperSettingsChange();
        RefreshPortraitPreview();
    }

    private void SplitDuoButton_Click(object sender, RoutedEventArgs e)
    {
        _splitDuoMode = true;
        EnsureSplitDuoRegions();
        SyncSplitDividerRatioFromRegions();
        ApplyDividerRatioToSplitRegions();

        UpdateSplitModeButtons();
        UpdateSplitSelectionOverlay();
        UpdateSplitPortraitPreview();
        CommitClipperSettingsChange();
        RefreshPortraitPreview();
    }

    private void SplitLayoutEditButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedCropMode() != CropMode.SplitLayout)
        {
            return;
        }

        var nextMode = IsSplitLayoutEditingActive()
            ? SplitEditorInteractionMode.Elements
            : SplitEditorInteractionMode.Layout;

        SetSplitEditorInteractionMode(nextMode);

        if (nextMode == SplitEditorInteractionMode.Elements)
        {
            CommitClipperSettingsChange();
            RefreshPortraitPreview();
        }
    }

    private void SetSplitEditorInteractionMode(SplitEditorInteractionMode mode)
    {
        if (_splitEditorInteractionMode == mode)
        {
            return;
        }

        _splitEditorInteractionMode = mode;
        if (_splitEditorInteractionMode != SplitEditorInteractionMode.Layout)
        {
            _splitDragMode = SplitDragMode.None;
            _splitResizeCorner = SplitResizeCorner.None;
            _splitDragStartRect = null;
            SplitSelectionCanvas.ReleaseMouseCapture();
            _isDraggingSplitDivider = false;
            SplitPortraitDividerCanvas.ReleaseMouseCapture();
        }

        UpdateSplitModeButtons();
        UpdateSplitSelectionOverlay();
        UpdateSplitPortraitPreview();
        UpdateSubtitlePlacementPreview();
        UpdateLogoPosition();
    }

    private bool IsSplitLayoutEditingActive()
    {
        return _splitEditorInteractionMode == SplitEditorInteractionMode.Layout;
    }

    private bool IsSplitElementEditingActive()
    {
        return _splitEditorInteractionMode == SplitEditorInteractionMode.Elements;
    }

    private void UpdateSplitModeButtons()
    {
        var isSplit = GetSelectedCropMode() == CropMode.SplitLayout;
        var isLayoutMode = isSplit && IsSplitLayoutEditingActive();

        SplitLayoutEditButton.Visibility = isSplit ? Visibility.Visible : Visibility.Collapsed;
        SplitSoloButton.Visibility = isLayoutMode ? Visibility.Visible : Visibility.Collapsed;
        SplitDuoButton.Visibility = isLayoutMode ? Visibility.Visible : Visibility.Collapsed;

        if (!isSplit)
        {
            SplitPortraitDividerCanvas.Visibility = Visibility.Collapsed;
            SplitPortraitDividerCanvas.IsHitTestVisible = false;
            SplitSelectionCanvas.IsHitTestVisible = false;
            _isDraggingSplitDivider = false;
            SplitPortraitDividerCanvas.ReleaseMouseCapture();
            return;
        }

        var modeResourceKey = isLayoutMode
            ? "Clipper.Split.EditMode.SaveLayout"
            : "Clipper.Split.EditMode.EditLayout";
        SplitLayoutEditButton.Content = FindResource(modeResourceKey);
        SplitLayoutEditButton.FontWeight = isLayoutMode ? FontWeights.SemiBold : FontWeights.Normal;
        SplitSoloButton.FontWeight = !_splitDuoMode ? FontWeights.SemiBold : FontWeights.Normal;
        SplitDuoButton.FontWeight = _splitDuoMode ? FontWeights.SemiBold : FontWeights.Normal;
        SplitSelectionCanvas.IsHitTestVisible = isLayoutMode;
        SplitPortraitDividerCanvas.IsHitTestVisible = _splitDuoMode && isLayoutMode;

        if (!isLayoutMode)
        {
            _isDraggingSplitDivider = false;
            SplitPortraitDividerCanvas.ReleaseMouseCapture();
        }
    }

    private void SplitSelectionCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateSplitSelectionOverlay();
    }

    private void SplitSelectionCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (GetSelectedCropMode() != CropMode.SplitLayout || !IsSplitLayoutEditingActive())
        {
            return;
        }

        var point = e.GetPosition(SplitSelectionCanvas);
        if (!TryHitSplitRegion(point, out var targetRegion, out var resizeCorner))
        {
            return;
        }

        _activeSplitRegion = targetRegion;
        _splitResizeCorner = resizeCorner;
        _splitDragMode = resizeCorner == SplitResizeCorner.None ? SplitDragMode.Move : SplitDragMode.Resize;
        _splitDragStart = point;
        _splitDragStartRect = GetSplitRegion(targetRegion).Clone();
        SplitSelectionCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void SplitSelectionCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_splitDragMode == SplitDragMode.None || _splitDragStartRect is null)
        {
            return;
        }

        var viewport = GetSplitImageViewportRect();
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return;
        }

        var current = e.GetPosition(SplitSelectionCanvas);
        var dx = (current.X - _splitDragStart.X) / viewport.Width;
        var dy = (current.Y - _splitDragStart.Y) / viewport.Height;
        var updated = _splitDragStartRect.Clone();

        if (_splitDragMode == SplitDragMode.Move)
        {
            updated.X = Math.Clamp(_splitDragStartRect.X + dx, 0, 1 - _splitDragStartRect.Width);
            updated.Y = Math.Clamp(_splitDragStartRect.Y + dy, 0, 1 - _splitDragStartRect.Height);
        }
        else
        {
            updated = ResizeSplitRegionFromCorner(_splitDragStartRect, _splitResizeCorner, dx, dy);
        }

        SetSplitRegion(_activeSplitRegion, updated.ClampToCanvas(0.05, 1.0));
        UpdateSplitSelectionOverlay();
        UpdateSplitPortraitPreview();
    }

    private void SplitSelectionCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_splitDragMode == SplitDragMode.None)
        {
            return;
        }

        _splitDragMode = SplitDragMode.None;
        _splitResizeCorner = SplitResizeCorner.None;
        _splitDragStartRect = null;
        SplitSelectionCanvas.ReleaseMouseCapture();
        CommitClipperSettingsChange();
        RefreshPortraitPreview();
    }

    private void SplitPortraitDividerHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_splitDuoMode || !IsSplitLayoutEditingActive())
        {
            return;
        }

        _isDraggingSplitDivider = true;
        SplitPortraitDividerCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void SplitPortraitDividerCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingSplitDivider || !_splitDuoMode || !IsSplitLayoutEditingActive())
        {
            return;
        }

        var height = SplitPortraitDividerCanvas.ActualHeight;
        if (height <= 0)
        {
            return;
        }

        var position = e.GetPosition(SplitPortraitDividerCanvas);
        _splitDividerRatio = ClampSplitDividerRatio(position.Y / height);
        ApplyDividerRatioToSplitRegions();
        UpdateSplitSelectionOverlay();
        UpdateSplitPortraitPreview();
        e.Handled = true;
    }

    private void SplitPortraitDividerCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingSplitDivider)
        {
            return;
        }

        _isDraggingSplitDivider = false;
        SplitPortraitDividerCanvas.ReleaseMouseCapture();
        CommitClipperSettingsChange();
        RefreshPortraitPreview();
        e.Handled = true;
    }

    private void SplitPortraitDividerCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateSplitDividerVisual();
    }

    private void EnsureSplitDuoRegions()
    {
        _splitPrimaryRegion ??= new NormalizedRect { X = 0.0, Y = 0.0, Width = 1.0, Height = 0.5 };
        _splitSecondaryRegion ??= new NormalizedRect { X = 0.0, Y = 0.5, Width = 1.0, Height = 0.5 };
        _splitPrimaryRegion.ClampToCanvas(0.05, 1.0);
        _splitSecondaryRegion.ClampToCanvas(0.05, 1.0);

        if (_splitPrimaryRegion.Width <= 0 || _splitPrimaryRegion.Height <= 0)
        {
            _splitPrimaryRegion = new NormalizedRect { X = 0.0, Y = 0.0, Width = 1.0, Height = 0.5 };
        }

        if (_splitSecondaryRegion.Width <= 0 || _splitSecondaryRegion.Height <= 0)
        {
            _splitSecondaryRegion = new NormalizedRect { X = 0.0, Y = 0.5, Width = 1.0, Height = 0.5 };
        }
    }

    private void SyncSplitDividerRatioFromRegions()
    {
        if (!_splitDuoMode)
        {
            return;
        }

        var primaryHeight = Math.Max(MinSplitDividerRatio, _splitPrimaryRegion.Height);
        var secondaryHeight = Math.Max(MinSplitDividerRatio, _splitSecondaryRegion.Height);
        var total = primaryHeight + secondaryHeight;
        _splitDividerRatio = total <= 0
            ? 0.5
            : ClampSplitDividerRatio(primaryHeight / total);
    }

    private void ApplyDividerRatioToSplitRegions()
    {
        if (!_splitDuoMode)
        {
            return;
        }

        EnsureSplitDuoRegions();

        var ratio = ClampSplitDividerRatio(_splitDividerRatio);
        var primary = _splitPrimaryRegion.Clone();
        var secondary = _splitSecondaryRegion.Clone();

        const double minSize = 0.05;
        var totalHeight = Math.Clamp(primary.Height + secondary.Height, minSize * 2.0, 1.8);
        var desiredPrimaryHeight = Math.Clamp(totalHeight * ratio, minSize, 1.0);
        var desiredSecondaryHeight = Math.Clamp(totalHeight - desiredPrimaryHeight, minSize, 1.0);

        if (desiredPrimaryHeight + desiredSecondaryHeight < minSize * 2.0)
        {
            desiredPrimaryHeight = ratio;
            desiredSecondaryHeight = 1.0 - ratio;
        }

        primary = ResizeRegionKeepingCenter(primary, desiredPrimaryHeight);
        secondary = ResizeRegionKeepingCenter(secondary, desiredSecondaryHeight);

        _splitDividerRatio = ratio;
        _splitPrimaryRegion = primary;
        _splitSecondaryRegion = secondary;
    }

    private void UpdateSplitDividerVisual()
    {
        if (!_splitDuoMode || !IsSplitLayoutEditingActive())
        {
            SplitPortraitDividerCanvas.Visibility = Visibility.Collapsed;
            return;
        }

        SplitPortraitDividerCanvas.Visibility = Visibility.Visible;
        var canvasHeight = SplitPortraitDividerCanvas.ActualHeight;
        var canvasWidth = SplitPortraitDividerCanvas.ActualWidth;
        if (canvasHeight <= 0 || canvasWidth <= 0)
        {
            return;
        }

        var ratio = ClampSplitDividerRatio(_splitDividerRatio);
        var handleTop = (ratio * canvasHeight) - (SplitPortraitDividerHandle.Height / 2.0);
        var maxTop = Math.Max(0, canvasHeight - SplitPortraitDividerHandle.Height);
        handleTop = Math.Clamp(handleTop, 0.0, maxTop);

        Canvas.SetTop(SplitPortraitDividerHandle, handleTop);
        Canvas.SetLeft(SplitPortraitDividerHandle, Math.Max(0.0, (canvasWidth - SplitPortraitDividerHandle.Width) / 2.0));
    }

    private static double ClampSplitDividerRatio(double ratio)
    {
        return Math.Clamp(ratio, MinSplitDividerRatio, MaxSplitDividerRatio);
    }

    private static NormalizedRect ResizeRegionKeepingCenter(NormalizedRect region, double newHeight)
    {
        var safe = (region ?? NormalizedRect.FullFrame()).Clone().ClampToCanvas(0.05, 1.0);
        var targetHeight = Math.Clamp(newHeight, 0.05, 1.0);
        var centerY = safe.Y + (safe.Height / 2.0);

        safe.Height = targetHeight;
        safe.Y = centerY - (targetHeight / 2.0);
        safe.X = Math.Clamp(safe.X, 0.0, 1.0 - safe.Width);
        return safe.ClampToCanvas(0.05, 1.0);
    }

    private void UpdateSplitSelectionOverlay()
    {
        if (GetSelectedCropMode() != CropMode.SplitLayout || !IsSplitLayoutEditingActive())
        {
            ApplyRegionToOverlay(
                _splitPrimaryRegion,
                SplitPrimarySelectionBox,
                SplitPrimaryResizeHandleTopLeft,
                SplitPrimaryResizeHandleTopRight,
                SplitPrimaryResizeHandleBottomLeft,
                SplitPrimaryResizeHandleBottomRight,
                Visibility.Collapsed);
            ApplyRegionToOverlay(
                _splitSecondaryRegion,
                SplitSecondarySelectionBox,
                SplitSecondaryResizeHandleTopLeft,
                SplitSecondaryResizeHandleTopRight,
                SplitSecondaryResizeHandleBottomLeft,
                SplitSecondaryResizeHandleBottomRight,
                Visibility.Collapsed);
            return;
        }

        var viewport = GetSplitImageViewportRect();
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return;
        }

        ApplyRegionToOverlay(
            _splitPrimaryRegion,
            SplitPrimarySelectionBox,
            SplitPrimaryResizeHandleTopLeft,
            SplitPrimaryResizeHandleTopRight,
            SplitPrimaryResizeHandleBottomLeft,
            SplitPrimaryResizeHandleBottomRight,
            Visibility.Visible);
        ApplyRegionToOverlay(
            _splitSecondaryRegion,
            SplitSecondarySelectionBox,
            SplitSecondaryResizeHandleTopLeft,
            SplitSecondaryResizeHandleTopRight,
            SplitSecondaryResizeHandleBottomLeft,
            SplitSecondaryResizeHandleBottomRight,
            _splitDuoMode ? Visibility.Visible : Visibility.Collapsed);
    }

    private void ApplyRegionToOverlay(
        NormalizedRect region,
        Border box,
        Border handleTopLeft,
        Border handleTopRight,
        Border handleBottomLeft,
        Border handleBottomRight,
        Visibility visibility)
    {
        box.Visibility = visibility;
        handleTopLeft.Visibility = visibility;
        handleTopRight.Visibility = visibility;
        handleBottomLeft.Visibility = visibility;
        handleBottomRight.Visibility = visibility;
        if (visibility != Visibility.Visible)
        {
            return;
        }

        var rect = GetCanvasRectForRegion(region);
        Canvas.SetLeft(box, rect.X);
        Canvas.SetTop(box, rect.Y);
        box.Width = rect.Width;
        box.Height = rect.Height;

        PositionCornerHandle(handleTopLeft, rect.X, rect.Y);
        PositionCornerHandle(handleTopRight, rect.Right, rect.Y);
        PositionCornerHandle(handleBottomLeft, rect.X, rect.Bottom);
        PositionCornerHandle(handleBottomRight, rect.Right, rect.Bottom);
    }

    private Rect GetCanvasRectForRegion(NormalizedRect region)
    {
        var viewport = GetSplitImageViewportRect();
        var normalized = (region ?? NormalizedRect.FullFrame()).Clone().ClampToCanvas(0.05, 1.0);
        return new Rect(
            viewport.X + (normalized.X * viewport.Width),
            viewport.Y + (normalized.Y * viewport.Height),
            normalized.Width * viewport.Width,
            normalized.Height * viewport.Height);
    }

    private bool TryHitSplitRegion(Point point, out SplitRegionTarget region, out SplitResizeCorner resizeCorner)
    {
        var viewport = GetSplitImageViewportRect();
        if (viewport.Width <= 0 || viewport.Height <= 0 || !viewport.Contains(point))
        {
            resizeCorner = SplitResizeCorner.None;
            region = SplitRegionTarget.Primary;
            return false;
        }

        var primaryHit = TryHitSingleRegion(point, _splitPrimaryRegion, out var primaryCorner);
        var secondaryCorner = SplitResizeCorner.None;
        var secondaryHit = _splitDuoMode && TryHitSingleRegion(point, _splitSecondaryRegion, out secondaryCorner);

        if (!primaryHit && !secondaryHit)
        {
            resizeCorner = SplitResizeCorner.None;
            region = SplitRegionTarget.Primary;
            return false;
        }

        if (primaryHit && !secondaryHit)
        {
            region = SplitRegionTarget.Primary;
            resizeCorner = primaryCorner;
            return true;
        }

        if (secondaryHit && !primaryHit)
        {
            region = SplitRegionTarget.Secondary;
            resizeCorner = secondaryCorner;
            return true;
        }

        // Beide Regionen getroffen: priorisiere aktive Region, außer nur eine hat einen Resize-Hit.
        var primaryIsResize = primaryCorner != SplitResizeCorner.None;
        var secondaryIsResize = secondaryCorner != SplitResizeCorner.None;

        if (primaryIsResize && !secondaryIsResize)
        {
            region = SplitRegionTarget.Primary;
            resizeCorner = primaryCorner;
            return true;
        }

        if (secondaryIsResize && !primaryIsResize)
        {
            region = SplitRegionTarget.Secondary;
            resizeCorner = secondaryCorner;
            return true;
        }

        if (_activeSplitRegion == SplitRegionTarget.Secondary)
        {
            region = SplitRegionTarget.Secondary;
            resizeCorner = secondaryCorner;
            return true;
        }

        region = SplitRegionTarget.Primary;
        resizeCorner = primaryCorner;
        return true;
    }

    private bool TryHitSingleRegion(Point point, NormalizedRect region, out SplitResizeCorner resizeCorner)
    {
        var rect = GetCanvasRectForRegion(region);
        var topLeftHandle = CreateHandleHitRect(rect.X, rect.Y);
        var topRightHandle = CreateHandleHitRect(rect.Right, rect.Y);
        var bottomLeftHandle = CreateHandleHitRect(rect.X, rect.Bottom);
        var bottomRightHandle = CreateHandleHitRect(rect.Right, rect.Bottom);

        if (topLeftHandle.Contains(point))
        {
            resizeCorner = SplitResizeCorner.TopLeft;
            return true;
        }
        if (topRightHandle.Contains(point))
        {
            resizeCorner = SplitResizeCorner.TopRight;
            return true;
        }
        if (bottomLeftHandle.Contains(point))
        {
            resizeCorner = SplitResizeCorner.BottomLeft;
            return true;
        }
        if (bottomRightHandle.Contains(point))
        {
            resizeCorner = SplitResizeCorner.BottomRight;
            return true;
        }

        resizeCorner = SplitResizeCorner.None;
        return rect.Contains(point);
    }

    private static Rect CreateHandleHitRect(double centerX, double centerY)
    {
        const double size = 16.0;
        var half = size / 2.0;
        return new Rect(centerX - half, centerY - half, size, size);
    }

    private static void PositionCornerHandle(Border handle, double cornerX, double cornerY)
    {
        var halfWidth = handle.Width / 2.0;
        var halfHeight = handle.Height / 2.0;
        Canvas.SetLeft(handle, cornerX - halfWidth);
        Canvas.SetTop(handle, cornerY - halfHeight);
    }

    private static NormalizedRect ResizeSplitRegionFromCorner(
        NormalizedRect start,
        SplitResizeCorner corner,
        double dx,
        double dy)
    {
        const double minSize = 0.05;

        var x1 = start.X;
        var y1 = start.Y;
        var x2 = start.X + start.Width;
        var y2 = start.Y + start.Height;

        switch (corner)
        {
            case SplitResizeCorner.TopLeft:
                x1 += dx;
                y1 += dy;
                break;
            case SplitResizeCorner.TopRight:
                x2 += dx;
                y1 += dy;
                break;
            case SplitResizeCorner.BottomLeft:
                x1 += dx;
                y2 += dy;
                break;
            case SplitResizeCorner.BottomRight:
                x2 += dx;
                y2 += dy;
                break;
            default:
                break;
        }

        x1 = Math.Clamp(x1, 0, 1);
        y1 = Math.Clamp(y1, 0, 1);
        x2 = Math.Clamp(x2, 0, 1);
        y2 = Math.Clamp(y2, 0, 1);

        if (x2 - x1 < minSize)
        {
            if (corner == SplitResizeCorner.TopLeft || corner == SplitResizeCorner.BottomLeft)
            {
                x1 = x2 - minSize;
            }
            else
            {
                x2 = x1 + minSize;
            }
        }

        if (y2 - y1 < minSize)
        {
            if (corner == SplitResizeCorner.TopLeft || corner == SplitResizeCorner.TopRight)
            {
                y1 = y2 - minSize;
            }
            else
            {
                y2 = y1 + minSize;
            }
        }

        x1 = Math.Clamp(x1, 0, 1 - minSize);
        y1 = Math.Clamp(y1, 0, 1 - minSize);
        x2 = Math.Clamp(x2, minSize, 1);
        y2 = Math.Clamp(y2, minSize, 1);

        if (x2 <= x1)
        {
            x2 = Math.Min(1, x1 + minSize);
        }
        if (y2 <= y1)
        {
            y2 = Math.Min(1, y1 + minSize);
        }

        return new NormalizedRect
        {
            X = x1,
            Y = y1,
            Width = x2 - x1,
            Height = y2 - y1
        };
    }

    private NormalizedRect GetSplitRegion(SplitRegionTarget target)
    {
        return target == SplitRegionTarget.Secondary ? _splitSecondaryRegion : _splitPrimaryRegion;
    }

    private void SetSplitRegion(SplitRegionTarget target, NormalizedRect value)
    {
        if (target == SplitRegionTarget.Secondary)
        {
            _splitSecondaryRegion = value;
        }
        else
        {
            _splitPrimaryRegion = value;
        }
    }

    private Rect GetSplitImageViewportRect()
    {
        var canvasWidth = SplitSelectionCanvas.ActualWidth;
        var canvasHeight = SplitSelectionCanvas.ActualHeight;
        if (canvasWidth <= 0 || canvasHeight <= 0)
        {
            return Rect.Empty;
        }

        if (_splitSourceFrame is null || _splitSourceFrame.PixelWidth <= 0 || _splitSourceFrame.PixelHeight <= 0)
        {
            return new Rect(0, 0, canvasWidth, canvasHeight);
        }

        var sourceAspect = (double)_splitSourceFrame.PixelWidth / _splitSourceFrame.PixelHeight;
        var canvasAspect = canvasWidth / canvasHeight;

        if (sourceAspect >= canvasAspect)
        {
            var height = canvasWidth / sourceAspect;
            return new Rect(0, (canvasHeight - height) / 2.0, canvasWidth, height);
        }
        else
        {
            var width = canvasHeight * sourceAspect;
            return new Rect((canvasWidth - width) / 2.0, 0, width, canvasHeight);
        }
    }

    private void UpdateSplitPortraitPreview()
    {
        if (!_isPlaying)
        {
            SplitPlaybackImage.Source = null;
            SplitPlaybackImage.Visibility = Visibility.Collapsed;
        }

        if (_splitSourceFrame is null)
        {
            SplitPortraitSingleImage.Source = null;
            SplitPortraitTopImage.Source = null;
            SplitPortraitBottomImage.Source = null;
            SplitPortraitDividerCanvas.Visibility = Visibility.Collapsed;
            SplitPortraitDividerCanvas.IsHitTestVisible = false;
            return;
        }

        var primary = CreateSplitCroppedBitmap(_splitSourceFrame, _splitPrimaryRegion);
        if (!_splitDuoMode)
        {
            SplitPortraitDuoGrid.Visibility = Visibility.Collapsed;
            SplitPortraitDividerCanvas.Visibility = Visibility.Collapsed;
            SplitPortraitDividerCanvas.IsHitTestVisible = false;
            SplitPortraitSingleImage.Visibility = Visibility.Visible;
            SplitPortraitSingleImage.Source = primary;
            SplitPortraitTopImage.Source = null;
            SplitPortraitBottomImage.Source = null;
            return;
        }

        var secondary = CreateSplitCroppedBitmap(_splitSourceFrame, _splitSecondaryRegion);
        var ratio = ClampSplitDividerRatio(_splitDividerRatio);
        SplitPortraitTopRow.Height = new GridLength(ratio, GridUnitType.Star);
        SplitPortraitBottomRow.Height = new GridLength(1.0 - ratio, GridUnitType.Star);
        SplitPortraitSingleImage.Visibility = Visibility.Collapsed;
        SplitPortraitDuoGrid.Visibility = Visibility.Visible;
        var showDivider = IsSplitLayoutEditingActive();
        SplitPortraitDividerCanvas.Visibility = showDivider ? Visibility.Visible : Visibility.Collapsed;
        SplitPortraitDividerCanvas.IsHitTestVisible = showDivider;
        SplitPortraitTopImage.Source = primary;
        SplitPortraitBottomImage.Source = secondary;
        UpdateSplitDividerVisual();
    }

    private static BitmapSource? CreateSplitCroppedBitmap(BitmapSource source, NormalizedRect region)
    {
        if (source.PixelWidth <= 0 || source.PixelHeight <= 0)
        {
            return null;
        }

        var normalized = (region ?? NormalizedRect.FullFrame()).Clone().ClampToCanvas(0.05, 1.0);
        var x = (int)Math.Round(normalized.X * source.PixelWidth);
        var y = (int)Math.Round(normalized.Y * source.PixelHeight);
        var width = Math.Max(1, (int)Math.Round(normalized.Width * source.PixelWidth));
        var height = Math.Max(1, (int)Math.Round(normalized.Height * source.PixelHeight));

        if (x + width > source.PixelWidth)
        {
            width = source.PixelWidth - x;
        }
        if (y + height > source.PixelHeight)
        {
            height = source.PixelHeight - y;
        }

        if (width <= 0 || height <= 0)
        {
            return null;
        }

        return new CroppedBitmap(source, new Int32Rect(x, y, width, height));
    }

    private SplitLayoutConfig BuildSplitLayoutFromEditor()
    {
        var preset = _splitDuoMode
            ? GetSelectedSplitPreset()
            : SplitLayoutPreset.Solo;

        if (_splitDuoMode && preset == SplitLayoutPreset.Solo)
        {
            preset = SplitLayoutPreset.TopBottom;
        }

        return new SplitLayoutConfig
        {
            Enabled = GetSelectedCropMode() == CropMode.SplitLayout,
            Preset = preset,
            PrimaryRegion = _splitPrimaryRegion.Clone().ClampToCanvas(0.05, 1.0),
            SecondaryRegion = (_splitDuoMode ? _splitSecondaryRegion : _splitPrimaryRegion).Clone().ClampToCanvas(0.05, 1.0)
        };
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

    private (Canvas Canvas, Border Preview) ResolveSubtitleOverlayTarget(object sender)
    {
        if (ReferenceEquals(sender, SplitSubtitleOverlayCanvas) || ReferenceEquals(sender, SplitSubtitlePlacementPreview))
        {
            return (SplitSubtitleOverlayCanvas, SplitSubtitlePlacementPreview);
        }

        return (SubtitleOverlayCanvas, SubtitlePlacementPreview);
    }

    private IEnumerable<(Canvas Canvas, Border Preview, bool IsSplitTarget)> EnumerateSubtitleOverlayTargets()
    {
        yield return (SubtitleOverlayCanvas, SubtitlePlacementPreview, false);
        yield return (SplitSubtitleOverlayCanvas, SplitSubtitlePlacementPreview, true);
    }

    private void SubtitleOverlayCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (EnableSubtitlesCheckBox.IsChecked != true)
        {
            return;
        }

        var (canvas, preview) = ResolveSubtitleOverlayTarget(sender);
        if (preview.Visibility != Visibility.Visible || canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0)
        {
            return;
        }

        var point = e.GetPosition(canvas);
        var previewSize = MeasureSubtitlePreview(preview);
        var left = Canvas.GetLeft(preview);
        var top = Canvas.GetTop(preview);
        if (double.IsNaN(left))
        {
            left = 0;
        }
        if (double.IsNaN(top))
        {
            top = 0;
        }

        if (point.X < left || point.X > left + previewSize.Width || point.Y < top || point.Y > top + previewSize.Height)
        {
            return;
        }

        _isDraggingSubtitle = true;
        _activeSubtitleCanvas = canvas;
        _activeSubtitlePreview = preview;
        _subtitleDragStart = point;
        _subtitleStartLeft = left;
        _subtitleStartTop = top;

        canvas.CaptureMouse();
        e.Handled = true;
    }

    private void SubtitleOverlayCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingSubtitle || EnableSubtitlesCheckBox.IsChecked != true || _activeSubtitleCanvas is null || _activeSubtitlePreview is null)
        {
            return;
        }

        var canvasWidth = _activeSubtitleCanvas.ActualWidth;
        var canvasHeight = _activeSubtitleCanvas.ActualHeight;
        if (canvasWidth <= 0 || canvasHeight <= 0)
        {
            return;
        }

        var previewSize = MeasureSubtitlePreview(_activeSubtitlePreview);
        var maxLeft = Math.Max(0, canvasWidth - previewSize.Width);
        var maxTop = Math.Max(0, canvasHeight - previewSize.Height);

        var current = e.GetPosition(_activeSubtitleCanvas);
        var left = Math.Clamp(_subtitleStartLeft + (current.X - _subtitleDragStart.X), 0, maxLeft);
        var top = Math.Clamp(_subtitleStartTop + (current.Y - _subtitleDragStart.Y), 0, maxTop);

        _isApplyingSettings = true;
        try
        {
            Canvas.SetLeft(_activeSubtitlePreview, left);
            Canvas.SetTop(_activeSubtitlePreview, top);

            var centerX = (left + previewSize.Width / 2.0) / canvasWidth;
            var centerY = (top + previewSize.Height / 2.0) / canvasHeight;
            SubtitlePositionXSlider.Value = Math.Clamp(centerX, 0, 1);
            SubtitlePositionYSlider.Value = Math.Clamp(centerY, 0, 1);
        }
        finally
        {
            _isApplyingSettings = false;
        }

        UpdateSubtitlePlacementPreview();
    }

    private void SubtitleOverlayCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingSubtitle)
        {
            return;
        }

        _isDraggingSubtitle = false;
        _activeSubtitleCanvas?.ReleaseMouseCapture();
        _activeSubtitleCanvas = null;
        _activeSubtitlePreview = null;
        UpdateSettingsValueLabels();
        CommitClipperSettingsChange();
        e.Handled = true;
    }

    private void SubtitleOverlayCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateSubtitlePlacementPreview();
    }

    private void UpdateSubtitlePlacementPreview()
    {
        if (!IsLoaded)
        {
            return;
        }

        UpdateSettingsValueLabels();

        var isVisible = EnableSubtitlesCheckBox.IsChecked == true && VideoPlayerPanel.Visibility == Visibility.Visible;
        var isSplit = GetSelectedCropMode() == CropMode.SplitLayout;

        foreach (var (canvas, preview, isSplitTarget) in EnumerateSubtitleOverlayTargets())
        {
            if (preview.Child is TextBlock previewText)
            {
                previewText.FontSize = Math.Max(12, SubtitleFontSizeSlider.Value * 0.45);
            }

            var targetVisible = isVisible && (isSplitTarget == isSplit);
            var canEdit = !isSplit || IsSplitElementEditingActive();
            canvas.IsHitTestVisible = targetVisible && canEdit;
            preview.Visibility = targetVisible ? Visibility.Visible : Visibility.Collapsed;

            if (!targetVisible)
            {
                continue;
            }

            var canvasWidth = canvas.ActualWidth;
            var canvasHeight = canvas.ActualHeight;
            if (canvasWidth <= 0 || canvasHeight <= 0)
            {
                continue;
            }

            var previewSize = MeasureSubtitlePreview(preview);
            var maxLeft = Math.Max(0, canvasWidth - previewSize.Width);
            var maxTop = Math.Max(0, canvasHeight - previewSize.Height);
            var left = Math.Clamp((SubtitlePositionXSlider.Value * canvasWidth) - (previewSize.Width / 2.0), 0, maxLeft);
            var top = Math.Clamp((SubtitlePositionYSlider.Value * canvasHeight) - (previewSize.Height / 2.0), 0, maxTop);

            Canvas.SetLeft(preview, left);
            Canvas.SetTop(preview, top);
        }
    }

    private void UpdateSettingsValueLabels()
    {
        SubtitleFontSizeValueText.Text = ((int)Math.Round(SubtitleFontSizeSlider.Value)).ToString(CultureInfo.CurrentCulture);
        SubtitlePositionXValueText.Text = SubtitlePositionXSlider.Value.ToString("0.00", CultureInfo.CurrentCulture);
        SubtitlePositionYValueText.Text = SubtitlePositionYSlider.Value.ToString("0.00", CultureInfo.CurrentCulture);
    }

    private void SubtitlePresetTopButton_Click(object sender, RoutedEventArgs e) => ApplySubtitlePreset(0.5, 0.20);

    private void SubtitlePresetMiddleButton_Click(object sender, RoutedEventArgs e) => ApplySubtitlePreset(0.5, 0.50);

    private void SubtitlePresetBottomButton_Click(object sender, RoutedEventArgs e) => ApplySubtitlePreset(0.5, 0.78);

    private void SubtitlePresetResetButton_Click(object sender, RoutedEventArgs e) => ApplySubtitlePreset(0.5, 0.70);

    private void SelectLogoButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Logo auswählen",
            Filter = "Bilder (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|Alle Dateien (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            SetLogoPath(dialog.FileName);
            CommitClipperSettingsChange();
        }
    }

    private void ClearLogoButton_Click(object sender, RoutedEventArgs e)
    {
        SetLogoPath(null);
        CommitClipperSettingsChange();
    }

    private void SetLogoPath(string? path)
    {
        _logoPath = path;

        if (string.IsNullOrWhiteSpace(path))
        {
            LogoPathText.Text = (string)FindResource("Clipper.Settings.NoLogo");
            ClearLogoButton.Visibility = Visibility.Collapsed;
            LogoOverlayCanvas.Visibility = Visibility.Collapsed;
            SplitLogoOverlayCanvas.Visibility = Visibility.Collapsed;
            LogoScaleLabel.Visibility = Visibility.Collapsed;
            LogoScalePanel.Visibility = Visibility.Collapsed;
            LogoDragHintText.Visibility = Visibility.Collapsed;
        }
        else
        {
            LogoPathText.Text = System.IO.Path.GetFileName(path);
            ClearLogoButton.Visibility = Visibility.Visible;
            LogoScaleLabel.Visibility = Visibility.Visible;
            LogoScalePanel.Visibility = Visibility.Visible;
            LogoDragHintText.Visibility = Visibility.Visible;
            UpdateLogoOverlay(path);
        }
    }

    private (Canvas Canvas, Image Image) ResolveLogoOverlayTarget(object sender)
    {
        if (ReferenceEquals(sender, SplitLogoOverlayImage) || ReferenceEquals(sender, SplitLogoOverlayCanvas))
        {
            return (SplitLogoOverlayCanvas, SplitLogoOverlayImage);
        }

        return (LogoOverlayCanvas, LogoOverlayImage);
    }

    private IEnumerable<(Canvas Canvas, Image Image, bool IsSplitTarget)> EnumerateLogoOverlayTargets()
    {
        yield return (LogoOverlayCanvas, LogoOverlayImage, false);
        yield return (SplitLogoOverlayCanvas, SplitLogoOverlayImage, true);
    }

    private void UpdateLogoOverlay(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            LogoOverlayCanvas.Visibility = Visibility.Collapsed;
            SplitLogoOverlayCanvas.Visibility = Visibility.Collapsed;
            LogoOverlayImage.Source = null;
            SplitLogoOverlayImage.Source = null;
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            LogoOverlayImage.Source = bitmap;
            SplitLogoOverlayImage.Source = bitmap;
            UpdateLogoPosition();
        }
        catch
        {
            LogoOverlayCanvas.Visibility = Visibility.Collapsed;
            SplitLogoOverlayCanvas.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateLogoPosition()
    {
        var hasLogo = !string.IsNullOrWhiteSpace(_logoPath) && File.Exists(_logoPath);
        var isPreviewVisible = VideoPlayerPanel.Visibility == Visibility.Visible;
        var isSplit = GetSelectedCropMode() == CropMode.SplitLayout;

        foreach (var (canvas, image, isSplitTarget) in EnumerateLogoOverlayTargets())
        {
            var targetVisible = hasLogo && isPreviewVisible && (isSplitTarget == isSplit);
            canvas.Visibility = targetVisible ? Visibility.Visible : Visibility.Collapsed;
            var canEdit = !isSplit || IsSplitElementEditingActive();
            canvas.IsHitTestVisible = targetVisible && canEdit;

            if (!targetVisible || canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0)
            {
                continue;
            }

            var logoSize = canvas.ActualWidth * _logoScale;
            image.Width = logoSize;
            image.Height = logoSize;

            var left = (_logoPositionX * canvas.ActualWidth) - (logoSize / 2);
            var top = (_logoPositionY * canvas.ActualHeight) - (logoSize / 2);
            left = Math.Clamp(left, 0, canvas.ActualWidth - logoSize);
            top = Math.Clamp(top, 0, canvas.ActualHeight - logoSize);

            Canvas.SetLeft(image, left);
            Canvas.SetTop(image, top);
        }
    }

    private void LogoOverlayCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateLogoPosition();
    }

    private void LogoScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Prüfe ob Controls bereits initialisiert sind
        if (LogoScaleValueText is null || LogoScaleSlider is null)
        {
            return;
        }

        if (_isApplyingSettings)
        {
            return;
        }

        _logoScale = LogoScaleSlider.Value;
        LogoScaleValueText.Text = $"{(int)(_logoScale * 100)}%";
        UpdateLogoPosition();
        CommitClipperSettingsChange();
    }

    private void LogoOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        var (canvas, image) = ResolveLogoOverlayTarget(sender);
        if (canvas.Visibility != Visibility.Visible || canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0)
        {
            return;
        }

        _isDraggingLogo = true;
        _activeLogoCanvas = canvas;
        _activeLogoImage = image;
        _logoDragStart = e.GetPosition(canvas);
        element.CaptureMouse();
        e.Handled = true;
    }

    private void LogoOverlay_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingLogo || _activeLogoCanvas is null || _activeLogoImage is null || _activeLogoCanvas.ActualWidth <= 0)
        {
            return;
        }

        var pos = e.GetPosition(_activeLogoCanvas);
        var logoSize = _activeLogoCanvas.ActualWidth * _logoScale;

        // Berechne neue Position (Zentrum des Logos)
        var newLeft = Canvas.GetLeft(_activeLogoImage) + (pos.X - _logoDragStart.X);
        var newTop = Canvas.GetTop(_activeLogoImage) + (pos.Y - _logoDragStart.Y);

        // Begrenze auf Canvas-Bereich
        newLeft = Math.Clamp(newLeft, 0, _activeLogoCanvas.ActualWidth - logoSize);
        newTop = Math.Clamp(newTop, 0, _activeLogoCanvas.ActualHeight - logoSize);

        Canvas.SetLeft(_activeLogoImage, newLeft);
        Canvas.SetTop(_activeLogoImage, newTop);

        // Speichere normalisierte Position (Zentrum)
        _logoPositionX = (newLeft + logoSize / 2) / _activeLogoCanvas.ActualWidth;
        _logoPositionY = (newTop + logoSize / 2) / _activeLogoCanvas.ActualHeight;

        _logoDragStart = pos;
        e.Handled = true;
    }

    private void LogoOverlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingLogo)
        {
            return;
        }

        _isDraggingLogo = false;
        _activeLogoCanvas = null;
        _activeLogoImage = null;
        (sender as FrameworkElement)?.ReleaseMouseCapture();
        CommitClipperSettingsChange();
        e.Handled = true;
    }

    public string? LogoPath => _logoPath;

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

        if (_editingCandidate is not null)
        {
            SaveActiveCandidateSettings();
            return;
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SaveActiveCandidateSettings()
    {
        if (_editingCandidate is null)
        {
            return;
        }

        _candidateSettings[_editingCandidate.Id] = CreateSettingsSnapshotFromControls();
    }

    private ClipperSettings CreateSettingsSnapshotFromControls()
    {
        var snapshot = _defaultClipperSettings.DeepCopy();
        snapshot.DefaultCropMode = GetSelectedCropMode();
        snapshot.ManualCropOffsetX = 0;
        var splitLayout = BuildSplitLayoutFromEditor();
        splitLayout.Enabled = snapshot.DefaultCropMode == CropMode.SplitLayout;
        snapshot.DefaultSplitLayout = splitLayout;
        snapshot.EnableSubtitlesByDefault = EnableSubtitlesCheckBox.IsChecked == true;
        snapshot.SubtitleSettings ??= new ClipSubtitleSettings();
        snapshot.SubtitleSettings.WordByWordHighlight = WordHighlightCheckBox.IsChecked == true;
        snapshot.SubtitleSettings.FontSize = (int)Math.Round(SubtitleFontSizeSlider.Value);
        snapshot.SubtitleSettings.PositionX = SubtitlePositionXSlider.Value;
        snapshot.SubtitleSettings.PositionY = SubtitlePositionYSlider.Value;
        return snapshot;
    }

    private static Size MeasureSubtitlePreview(Border preview)
    {
        preview.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var desired = preview.DesiredSize;
        return new Size(
            desired.Width <= 0 ? 40 : desired.Width,
            desired.Height <= 0 ? 20 : desired.Height);
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
            CroppedPreviewImage.Source = null;
            PreviewFrameImage.Source = null;
            PreviewFrameImage.Visibility = Visibility.Collapsed;
            _currentCropRegion = null;
            return;
        }

        // Cancel any ongoing face detection
        _faceDetectionCts?.Cancel();
        _faceDetectionCts = new CancellationTokenSource();
        var ct = _faceDetectionCts.Token;

        var cropMode = GetSelectedCropMode();
        var manualOffset = 0.0;
        var clipEnd = _currentPreviewCandidate?.End ?? clipStart + TimeSpan.FromSeconds(30);

        if (cropMode == CropMode.SplitLayout)
        {
            var frameCacheKey = BuildPreviewCacheKey(videoPath, clipStart);
            if (!_previewFrameCache.TryGetValue(frameCacheKey, out var sourceFrame))
            {
                sourceFrame = await Task.Run(() => ExtractFrameBitmap(videoPath, clipStart), ct);
                if (sourceFrame is not null)
                {
                    _previewFrameCache[frameCacheKey] = sourceFrame;
                }
            }

            if (ct.IsCancellationRequested)
            {
                return;
            }

            _currentCropRegion = null;
            _splitSourceFrame = sourceFrame;
            SplitSourceImage.Source = sourceFrame;
            var autoSelectionKey = BuildSplitFaceAutoSelectionKey(videoPath, clipStart);
            var shouldRunAutoSelection = ShouldRunInitialSplitAutoSelection(autoSelectionKey);
            var selectionApplied = false;

            if (shouldRunAutoSelection && sourceFrame is not null)
            {
                ApplyNeutralSplitInitialSelection(sourceFrame);
                selectionApplied = true;

                var autoApplied = await TryApplyAutoSplitFaceSelectionAsync(
                    videoPath,
                    clipStart,
                    clipEnd,
                    sourceFrame,
                    ct);
                selectionApplied = selectionApplied || autoApplied;
            }

            UpdatePreviewModePanels();
            UpdateSplitSelectionOverlay();
            UpdateSplitPortraitPreview();

            if (selectionApplied)
            {
                CommitClipperSettingsChange();
            }

            return;
        }

        _splitSourceFrame = null;
        SplitSourceImage.Source = null;
        SplitPortraitSingleImage.Source = null;
        SplitPortraitTopImage.Source = null;
        SplitPortraitBottomImage.Source = null;

        // Cache-Key für Portrait-Preview (inkl. Face-Detection)
        var portraitCacheKey = BuildPortraitPreviewCacheKey(videoPath, clipStart, cropMode, manualOffset);

        if (_portraitPreviewCache.TryGetValue(portraitCacheKey, out var cachedPortrait))
        {
            CroppedPreviewImage.Source = cachedPortrait;
            return;
        }

        var requestVersion = ++_portraitPreviewVersion;

        try
        {
            // Für AutoDetect: Face-Detection ausführen
            CropRegionResult? cropRegion = null;
            if (cropMode == CropMode.AutoDetect && _faceDetectionService.IsAvailable)
            {
                // Prüfen ob Face-Detection bereits gecached ist
                var faceDetectionKey = BuildFaceDetectionCacheKey(videoPath, clipStart, clipEnd);
                if (!_faceDetectionCache.TryGetValue(faceDetectionKey, out cropRegion))
                {
                    // Video-Dimensionen holen
                    var (sourceWidth, sourceHeight) = await Task.Run(() => GetVideoDimensions(videoPath), ct);

                    if (sourceWidth > 0 && sourceHeight > 0)
                    {
                        // Face-Detection ausführen
                        var analyses = await _faceDetectionService.AnalyzeVideoAsync(
                            videoPath,
                            TimeSpan.FromSeconds(FaceDetectionDefaults.SampleIntervalSeconds),
                            clipStart,
                            clipEnd,
                            ct);

                        if (analyses.Count > 0)
                        {
                            cropRegion = _faceDetectionService.CalculateCropRegion(
                                analyses,
                                new PixelSize(sourceWidth, sourceHeight),
                                new PixelSize(1080, 1920),
                                CropStrategy.MultipleFaces);

                            _faceDetectionCache[faceDetectionKey] = cropRegion;
                        }
                    }
                }
            }

            if (ct.IsCancellationRequested || requestVersion != _portraitPreviewVersion)
            {
                return;
            }

            // Speichere Crop-Region für Wiedergabe-Frames
            _currentCropRegion = cropRegion;

            // Frame mit Crop-Region extrahieren
            var portraitBitmap = await Task.Run(() =>
                ExtractPortraitFrameBitmapWithCropRegion(videoPath, clipStart, cropMode, manualOffset, cropRegion), ct);

            if (ct.IsCancellationRequested || requestVersion != _portraitPreviewVersion)
            {
                return;
            }

            if (portraitBitmap is not null)
            {
                _portraitPreviewCache[portraitCacheKey] = portraitBitmap;
                CroppedPreviewImage.Source = portraitBitmap;
            }
        }
        catch (OperationCanceledException)
        {
            // Abgebrochen - ignorieren
        }
        catch
        {
            // Fallback bei Fehler
        }
    }

    private static string BuildPortraitPreviewCacheKey(string videoPath, TimeSpan timestamp, CropMode cropMode, double manualOffset)
    {
        var ms = (long)Math.Round(timestamp.TotalMilliseconds, MidpointRounding.AwayFromZero);
        var offsetInt = (int)Math.Round(manualOffset * 100);
        return $"{videoPath}|{ms}|portrait|{cropMode}|{offsetInt}";
    }

    private static string BuildFaceDetectionCacheKey(string videoPath, TimeSpan clipStart, TimeSpan clipEnd)
    {
        var startMs = (long)Math.Round(clipStart.TotalMilliseconds, MidpointRounding.AwayFromZero);
        var endMs = (long)Math.Round(clipEnd.TotalMilliseconds, MidpointRounding.AwayFromZero);
        return $"{videoPath}|face|{startMs}|{endMs}";
    }

    private string BuildSplitFaceAutoSelectionKey(string videoPath, TimeSpan clipStart)
    {
        var startMs = (long)Math.Round(clipStart.TotalMilliseconds, MidpointRounding.AwayFromZero);
        var candidateKey = _editingCandidate?.Id.ToString("N") ?? "none";
        return $"{videoPath}|split-face-auto|{candidateKey}|{startMs}";
    }

    private static string BuildSplitFaceAnchorCacheKey(string videoPath, TimeSpan clipStart, TimeSpan clipEnd)
    {
        var startMs = (long)Math.Round(clipStart.TotalMilliseconds, MidpointRounding.AwayFromZero);
        var endMs = (long)Math.Round(clipEnd.TotalMilliseconds, MidpointRounding.AwayFromZero);
        return $"{videoPath}|split-face-anchors|{startMs}|{endMs}";
    }

    private bool ShouldRunInitialSplitAutoSelection(string autoSelectionKey)
    {
        if (_splitFaceAutoSelectionProcessedKeys.Contains(autoSelectionKey))
        {
            return false;
        }

        return _editingCandidate is not null
            && !_candidateSettings.ContainsKey(_editingCandidate.Id);
    }

    private void ApplyNeutralSplitInitialSelection(BitmapSource sourceFrame)
    {
        var sourceAspect = sourceFrame.PixelHeight > 0
            ? (double)sourceFrame.PixelWidth / sourceFrame.PixelHeight
            : (16.0 / 9.0);

        if (_splitDuoMode)
        {
            var topShare = ClampSplitDividerRatio(_splitDividerRatio);
            var bottomShare = 1.0 - topShare;

            var topAnchor = new SplitFaceAnchor(0.50, 0.34, 0.15, 0.20, 0);
            var bottomAnchor = new SplitFaceAnchor(0.50, 0.66, 0.15, 0.20, 0);
            _splitPrimaryRegion = CreateSplitRegionFromFaceAnchor(topAnchor, sourceAspect, topShare);
            _splitSecondaryRegion = CreateSplitRegionFromFaceAnchor(bottomAnchor, sourceAspect, bottomShare);
            return;
        }

        var soloAnchor = new SplitFaceAnchor(0.50, 0.50, 0.16, 0.22, 0);
        _splitPrimaryRegion = CreateSplitRegionFromFaceAnchor(soloAnchor, sourceAspect, 1.0);
    }

    private async Task<bool> TryApplyAutoSplitFaceSelectionAsync(
        string videoPath,
        TimeSpan clipStart,
        TimeSpan clipEnd,
        BitmapSource sourceFrame,
        CancellationToken ct)
    {
        var autoSelectionKey = BuildSplitFaceAutoSelectionKey(videoPath, clipStart);
        if (_splitFaceAutoSelectionProcessedKeys.Contains(autoSelectionKey))
        {
            return false;
        }

        if (_editingCandidate is null || _candidateSettings.ContainsKey(_editingCandidate.Id))
        {
            _splitFaceAutoSelectionProcessedKeys.Add(autoSelectionKey);
            return false;
        }

        if (!_faceDetectionService.IsAvailable)
        {
            _splitFaceAutoSelectionProcessedKeys.Add(autoSelectionKey);
            return false;
        }

        var quickDetectionEnd = clipStart + TimeSpan.FromSeconds(1.2);
        if (clipEnd > clipStart)
        {
            quickDetectionEnd = TimeSpan.FromTicks(Math.Min(quickDetectionEnd.Ticks, clipEnd.Ticks));
        }

        if (quickDetectionEnd <= clipStart)
        {
            quickDetectionEnd = clipStart + TimeSpan.FromMilliseconds(250);
        }

        var anchorCacheKey = BuildSplitFaceAnchorCacheKey(videoPath, clipStart, quickDetectionEnd);
        if (!_splitFaceAnchorCache.TryGetValue(anchorCacheKey, out var anchors))
        {
            var analyses = await _faceDetectionService.AnalyzeVideoAsync(
                videoPath,
                TimeSpan.FromSeconds(0.8),
                clipStart,
                quickDetectionEnd,
                ct);

            if (ct.IsCancellationRequested)
            {
                return false;
            }

            anchors = BuildSplitFaceAnchors(
                analyses,
                Math.Max(1, sourceFrame.PixelWidth),
                Math.Max(1, sourceFrame.PixelHeight));
            _splitFaceAnchorCache[anchorCacheKey] = anchors;
        }

        _splitFaceAutoSelectionProcessedKeys.Add(autoSelectionKey);

        if (anchors.Count == 0)
        {
            return false;
        }

        var primaryAnchor = anchors[0];
        var secondaryAnchor = FindSecondarySplitFaceAnchor(anchors, primaryAnchor);

        if (_splitDuoMode)
        {
            var resolvedSecondaryAnchor = secondaryAnchor ?? CreateOppositeSplitFaceAnchor(primaryAnchor);
            _splitPrimaryRegion = RecenterSplitRegionAroundFace(_splitPrimaryRegion, primaryAnchor);
            _splitSecondaryRegion = RecenterSplitRegionAroundFace(_splitSecondaryRegion, resolvedSecondaryAnchor);
        }
        else
        {
            _splitPrimaryRegion = RecenterSplitRegionAroundFace(_splitPrimaryRegion, primaryAnchor);
        }

        _splitPrimaryRegion.ClampToCanvas(0.05, 1.0);
        _splitSecondaryRegion.ClampToCanvas(0.05, 1.0);
        return true;
    }

    private static IReadOnlyList<SplitFaceAnchor> BuildSplitFaceAnchors(
        IReadOnlyList<FrameFaceAnalysis> analyses,
        int sourceWidth,
        int sourceHeight)
    {
        if (analyses is null || analyses.Count == 0 || sourceWidth <= 0 || sourceHeight <= 0)
        {
            return Array.Empty<SplitFaceAnchor>();
        }

        var samples = new List<SplitFaceSample>();
        foreach (var analysis in analyses)
        {
            if (analysis?.Faces is null || analysis.Faces.Count == 0)
            {
                continue;
            }

            foreach (var face in analysis.Faces)
            {
                if (face.Confidence <= 0)
                {
                    continue;
                }

                var width = Math.Clamp(face.BoundingBox.Width / sourceWidth, 0.01f, 1.0f);
                var height = Math.Clamp(face.BoundingBox.Height / sourceHeight, 0.01f, 1.0f);
                if (width > 0.72 || height > 0.92 || (width * height) > 0.42)
                {
                    continue;
                }
                var centerX = Math.Clamp(face.BoundingBox.CenterX / sourceWidth, 0.0f, 1.0f);
                var centerY = Math.Clamp(face.BoundingBox.CenterY / sourceHeight, 0.0f, 1.0f);
                samples.Add(new SplitFaceSample(centerX, centerY, width, height, face.Confidence));
            }
        }

        if (samples.Count == 0)
        {
            return Array.Empty<SplitFaceAnchor>();
        }

        const double clusterDistance = 0.16;
        var clusters = new List<SplitFaceClusterAccumulator>();
        foreach (var sample in samples.OrderByDescending(s => s.Weight))
        {
            SplitFaceClusterAccumulator? selectedCluster = null;
            var selectedDistance = double.MaxValue;

            foreach (var cluster in clusters)
            {
                var distance = cluster.DistanceTo(sample.CenterX, sample.CenterY);
                if (distance <= clusterDistance && distance < selectedDistance)
                {
                    selectedCluster = cluster;
                    selectedDistance = distance;
                }
            }

            if (selectedCluster is null)
            {
                clusters.Add(new SplitFaceClusterAccumulator(sample));
            }
            else
            {
                selectedCluster.Add(sample);
            }
        }

        return clusters
            .Select(c => c.ToAnchor())
            .OrderByDescending(a => a.Score)
            .Take(6)
            .ToList();
    }

    private static SplitFaceAnchor? FindSecondarySplitFaceAnchor(
        IReadOnlyList<SplitFaceAnchor> anchors,
        SplitFaceAnchor primary)
    {
        if (anchors.Count <= 1)
        {
            return null;
        }

        SplitFaceAnchor? best = null;
        var bestDistance = 0.0;
        foreach (var candidate in anchors.Skip(1))
        {
            var distance = SplitFaceAnchorDistance(primary, candidate);
            if (distance > bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        if (!best.HasValue)
        {
            return anchors[1];
        }

        if (bestDistance >= 0.08)
        {
            return best;
        }

        return anchors[1];
    }

    private static double SplitFaceAnchorDistance(SplitFaceAnchor a, SplitFaceAnchor b)
    {
        var dx = Math.Abs(a.CenterX - b.CenterX);
        var dy = Math.Abs(a.CenterY - b.CenterY);
        return (dx * 1.7) + dy;
    }

    private static SplitFaceAnchor CreateOppositeSplitFaceAnchor(SplitFaceAnchor primary)
    {
        var oppositeX = primary.CenterX < 0.5 ? 0.78 : 0.22;
        var oppositeY = Math.Clamp(primary.CenterY, 0.30, 0.70);
        return new SplitFaceAnchor(
            oppositeX,
            oppositeY,
            Math.Clamp(primary.FaceWidth, 0.08, 0.45),
            Math.Clamp(primary.FaceHeight, 0.08, 0.45),
            0);
    }

    private static NormalizedRect CreateSplitRegionFromFaceAnchor(
        SplitFaceAnchor anchor,
        double sourceAspect,
        double segmentShare)
    {
        var safeSourceAspect = sourceAspect > 0 ? sourceAspect : (16.0 / 9.0);
        var safeShare = Math.Clamp(segmentShare, 0.05, 1.0);
        var targetAspect = 9.0 / (16.0 * safeShare);

        var desiredHeight = Math.Clamp(
            Math.Max(anchor.FaceHeight * 2.8, 0.42 + (safeShare * 0.35)),
            0.35,
            0.90);
        var desiredWidth = desiredHeight * targetAspect / safeSourceAspect;

        var minWidthForFace = anchor.FaceWidth * 2.2;
        if (desiredWidth < minWidthForFace)
        {
            desiredWidth = minWidthForFace;
            desiredHeight = desiredWidth * safeSourceAspect / targetAspect;
        }

        if (desiredHeight > 0.92)
        {
            desiredHeight = 0.92;
            desiredWidth = desiredHeight * targetAspect / safeSourceAspect;
        }

        desiredWidth = Math.Clamp(desiredWidth, 0.18, 0.88);
        desiredHeight = Math.Clamp(desiredHeight, 0.30, 0.92);

        var x = anchor.CenterX - (desiredWidth / 2.0);
        var y = anchor.CenterY - (desiredHeight / 2.0);

        return new NormalizedRect
        {
            X = x,
            Y = y,
            Width = desiredWidth,
            Height = desiredHeight
        }.ClampToCanvas(0.05, 1.0);
    }

    private static NormalizedRect RecenterSplitRegionAroundFace(NormalizedRect region, SplitFaceAnchor anchor)
    {
        var safe = (region ?? NormalizedRect.FullFrame()).Clone().ClampToCanvas(0.05, 1.0);
        return new NormalizedRect
        {
            X = anchor.CenterX - (safe.Width / 2.0),
            Y = anchor.CenterY - (safe.Height / 2.0),
            Width = safe.Width,
            Height = safe.Height
        }.ClampToCanvas(0.05, 1.0);
    }

    private (int width, int height) GetVideoDimensions(string videoPath)
    {
        var ffmpegPath = FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            return (0, 0);
        }

        var ffprobePath = Path.Combine(Path.GetDirectoryName(ffmpegPath) ?? "", "ffprobe.exe");
        if (!File.Exists(ffprobePath))
        {
            return (1920, 1080); // Default fallback
        }

        try
        {
            var probeInfo = new ProcessStartInfo
            {
                FileName = ffprobePath,
                Arguments = $"-v error -select_streams v:0 -show_entries stream=width,height -of csv=p=0 \"{videoPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };

            using var probeProcess = Process.Start(probeInfo);
            if (probeProcess is not null)
            {
                var output = probeProcess.StandardOutput.ReadToEnd();
                probeProcess.WaitForExit(3000);
                var parts = output.Trim().Split(',');
                if (parts.Length >= 2 &&
                    int.TryParse(parts[0], out var w) &&
                    int.TryParse(parts[1], out var h))
                {
                    return (w, h);
                }
            }
        }
        catch
        {
            // Ignore
        }

        return (1920, 1080);
    }

    private BitmapImage? ExtractPortraitFrameBitmapWithCropRegion(
        string videoPath,
        TimeSpan timestamp,
        CropMode cropMode,
        double manualOffset,
        CropRegionResult? faceDetectionCropRegion)
    {
        var ffmpegPath = FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            return null;
        }

        try
        {
            var seconds = timestamp.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture);
            var (sourceWidth, sourceHeight) = GetVideoDimensions(videoPath);

            string cropFilter;
            if (cropMode == CropMode.AutoDetect && faceDetectionCropRegion is not null)
            {
                // Use face-detection crop region
                cropFilter = faceDetectionCropRegion.ToFfmpegCropFilter();
            }
            else
            {
                // Calculate crop filter based on mode
                cropFilter = CalculateCropFilter(sourceWidth, sourceHeight, cropMode, manualOffset);
            }

            // FFmpeg-Argumente für Portrait-Frame
            var filterChain = string.IsNullOrEmpty(cropFilter)
                ? "scale=1080:1920:force_original_aspect_ratio=decrease,pad=1080:1920:(ow-iw)/2:(oh-ih)/2:black"
                : $"{cropFilter},scale=1080:1920:force_original_aspect_ratio=decrease,pad=1080:1920:(ow-iw)/2:(oh-ih)/2:black";

            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-hide_banner -loglevel error -ss {seconds} -i \"{videoPath}\" -frames:v 1 -vf \"{filterChain}\" -f image2pipe -vcodec mjpeg pipe:1",
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
            process.WaitForExit(10000);
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

    private static string CalculateCropFilter(int sourceWidth, int sourceHeight, CropMode cropMode, double manualOffset)
    {
        if (cropMode == CropMode.None)
        {
            return string.Empty;
        }

        // Berechne Crop-Breite für 9:16 aus der Quellhöhe
        var targetRatio = 9.0 / 16.0;
        var cropWidth = (int)Math.Round(sourceHeight * targetRatio);

        if (cropWidth >= sourceWidth)
        {
            // Video ist bereits schmal genug, kein Crop nötig
            return string.Empty;
        }

        int cropX;
        switch (cropMode)
        {
            case CropMode.Center:
                cropX = (sourceWidth - cropWidth) / 2;
                break;
            case CropMode.Manual:
                var maxShift = (sourceWidth - cropWidth) / 2.0;
                var centerX = sourceWidth / 2.0 + Math.Clamp(manualOffset, -1.0, 1.0) * maxShift;
                cropX = (int)Math.Round(centerX - cropWidth / 2.0);
                cropX = Math.Clamp(cropX, 0, sourceWidth - cropWidth);
                break;
            case CropMode.SplitLayout:
                // Preview-Fallback: zeige zentrierten Portrait-Crop.
                cropX = (sourceWidth - cropWidth) / 2;
                break;
            case CropMode.AutoDetect:
            default:
                // Für AutoDetect ohne Face-Detection: Center als Fallback
                cropX = (sourceWidth - cropWidth) / 2;
                break;
        }

        return $"crop={cropWidth}:{sourceHeight}:{cropX}:0";
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

    private static void CopySettings(ClipperSettings source, ClipperSettings target)
    {
        if (source is null || target is null)
        {
            return;
        }

        var copy = source.DeepCopy();
        target.MinClipDurationSeconds = copy.MinClipDurationSeconds;
        target.MaxClipDurationSeconds = copy.MaxClipDurationSeconds;
        target.MaxCandidatesPerDraft = copy.MaxCandidatesPerDraft;
        target.DefaultCropMode = copy.DefaultCropMode;
        target.ManualCropOffsetX = copy.ManualCropOffsetX;
        target.EnableFaceDetection = copy.EnableFaceDetection;
        target.FaceDetectionFrameCount = copy.FaceDetectionFrameCount;
        target.FaceDetectionMinConfidence = copy.FaceDetectionMinConfidence;
        target.EnableSubtitlesByDefault = copy.EnableSubtitlesByDefault;
        target.SubtitleSettings = copy.SubtitleSettings;
        target.DefaultSplitLayout = copy.DefaultSplitLayout;
        target.DefaultLogoPath = copy.DefaultLogoPath;
        target.LogoPositionX = copy.LogoPositionX;
        target.LogoPositionY = copy.LogoPositionY;
        target.LogoScale = copy.LogoScale;
        target.OutputWidth = copy.OutputWidth;
        target.OutputHeight = copy.OutputHeight;
        target.VideoQuality = copy.VideoQuality;
        target.AudioBitrate = copy.AudioBitrate;
        target.AutoCreateDraftFromClip = copy.AutoCreateDraftFromClip;
        target.OpenOutputFolderAfterRender = copy.OpenOutputFolderAfterRender;
        target.UseCandidateCache = copy.UseCandidateCache;
        target.CustomScoringPrompt = copy.CustomScoringPrompt;
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

    /// <summary>
    /// Startet einen kontinuierlichen FFmpeg-Prozess der Frames mit Crop als MJPEG-Stream ausgibt.
    /// </summary>
    private void StartFfmpegPreviewProcess(string videoPath, TimeSpan start, TimeSpan end)
    {
        StopFfmpegPreviewProcess();

        var ffmpegPath = FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            return;
        }

        var cropMode = GetSelectedCropMode();
        var manualOffset = 0.0;
        var cropRegion = _currentCropRegion;
        var (sourceWidth, sourceHeight) = GetVideoDimensions(videoPath);

        string cropFilter;
        string filterChain;
        if (cropMode == CropMode.SplitLayout)
        {
            filterChain = BuildSplitPlaybackFilterChain(sourceWidth, sourceHeight);
        }
        else if (cropMode == CropMode.AutoDetect && cropRegion is not null)
        {
            cropFilter = cropRegion.ToFfmpegCropFilter();
            filterChain = string.IsNullOrEmpty(cropFilter)
                ? "scale=360:640:flags=fast_bilinear"
                : $"{cropFilter},scale=360:640:flags=fast_bilinear";
        }
        else
        {
            cropFilter = CalculateCropFilter(sourceWidth, sourceHeight, cropMode, manualOffset);
            filterChain = string.IsNullOrEmpty(cropFilter)
                ? "scale=360:640:flags=fast_bilinear"
                : $"{cropFilter},scale=360:640:flags=fast_bilinear";
        }

        var startSeconds = start.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture);
        var duration = (end - start).TotalSeconds.ToString("F3", CultureInfo.InvariantCulture);

        // FFmpeg: Kontinuierlicher MJPEG-Stream mit 24 FPS
        var args = $"-hide_banner -loglevel error -ss {startSeconds} -t {duration} -i \"{videoPath}\" " +
                   $"-vf \"{filterChain},fps=24\" -f image2pipe -vcodec mjpeg -q:v 8 pipe:1";

        _ffmpegPreviewCts = new CancellationTokenSource();
        var ct = _ffmpegPreviewCts.Token;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            lock (_ffmpegLock)
            {
                _ffmpegPreviewProcess = Process.Start(startInfo);
            }

            if (_ffmpegPreviewProcess is null)
            {
                return;
            }

            // Starte Task zum Lesen der Frames
            _ffmpegFrameReaderTask = Task.Run(() => ReadFfmpegFramesAsync(_ffmpegPreviewProcess, ct), ct);
        }
        catch
        {
            StopFfmpegPreviewProcess();
        }
    }

    private string BuildSplitPlaybackFilterChain(int sourceWidth, int sourceHeight)
    {
        var safeSourceWidth = Math.Max(1, sourceWidth);
        var safeSourceHeight = Math.Max(1, sourceHeight);
        var outputWidth = 360;
        var outputHeight = 640;

        var primary = ToSplitPlaybackCropRectangle(_splitPrimaryRegion, safeSourceWidth, safeSourceHeight);

        if (!_splitDuoMode)
        {
            return $"crop={primary.Width}:{primary.Height}:{primary.X}:{primary.Y}," +
                   $"scale={outputWidth}:{outputHeight}:force_original_aspect_ratio=increase," +
                   $"crop={outputWidth}:{outputHeight}";
        }

        var secondary = ToSplitPlaybackCropRectangle(_splitSecondaryRegion, safeSourceWidth, safeSourceHeight);
        var ratio = ClampSplitDividerRatio(_splitDividerRatio);
        var topHeight = Math.Clamp((int)Math.Round(outputHeight * ratio), 1, outputHeight - 1);
        var bottomHeight = Math.Max(1, outputHeight - topHeight);

        return $"split=2[v1][v2];" +
               $"[v1]crop={primary.Width}:{primary.Height}:{primary.X}:{primary.Y}," +
               $"scale={outputWidth}:{topHeight}:force_original_aspect_ratio=increase," +
               $"crop={outputWidth}:{topHeight}[upper];" +
               $"[v2]crop={secondary.Width}:{secondary.Height}:{secondary.X}:{secondary.Y}," +
               $"scale={outputWidth}:{bottomHeight}:force_original_aspect_ratio=increase," +
               $"crop={outputWidth}:{bottomHeight}[lower];" +
               $"[upper][lower]vstack=inputs=2";
    }

    private static CropRectangle ToSplitPlaybackCropRectangle(NormalizedRect region, int sourceWidth, int sourceHeight)
    {
        var safe = (region ?? NormalizedRect.FullFrame()).Clone().ClampToCanvas(0.05, 1.0);
        var width = Math.Clamp((int)Math.Round(safe.Width * sourceWidth), 1, sourceWidth);
        var height = Math.Clamp((int)Math.Round(safe.Height * sourceHeight), 1, sourceHeight);
        var maxX = Math.Max(0, sourceWidth - width);
        var maxY = Math.Max(0, sourceHeight - height);
        var x = Math.Clamp((int)Math.Round(safe.X * sourceWidth), 0, maxX);
        var y = Math.Clamp((int)Math.Round(safe.Y * sourceHeight), 0, maxY);
        return new CropRectangle(x, y, width, height);
    }

    private async Task ReadFfmpegFramesAsync(Process process, CancellationToken ct)
    {
        const int TargetFps = 24;
        const int FrameIntervalMs = 1000 / TargetFps; // ~42ms pro Frame

        try
        {
            using var stream = process.StandardOutput.BaseStream;
            var buffer = new byte[1024 * 1024]; // 1MB Buffer
            var frameBuffer = new MemoryStream();
            var lastFrameTime = DateTime.UtcNow;

            while (!ct.IsCancellationRequested && !process.HasExited)
            {
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct);
                if (bytesRead == 0)
                {
                    break;
                }

                // Schreibe in Frame-Buffer
                frameBuffer.Write(buffer, 0, bytesRead);

                // Suche nach JPEG-Ende-Marker (FFD9)
                var data = frameBuffer.ToArray();
                var frameEnd = FindJpegEndMarker(data);

                while (frameEnd >= 0)
                {
                    // Warte bis genug Zeit für nächstes Frame vergangen ist (Echtzeit-Sync)
                    var elapsed = (DateTime.UtcNow - lastFrameTime).TotalMilliseconds;
                    if (elapsed < FrameIntervalMs)
                    {
                        await Task.Delay((int)(FrameIntervalMs - elapsed), ct);
                    }
                    lastFrameTime = DateTime.UtcNow;

                    // Extrahiere Frame
                    var frameData = new byte[frameEnd + 2];
                    Array.Copy(data, frameData, frameEnd + 2);

                    // Zeige Frame auf UI-Thread
                    if (!ct.IsCancellationRequested)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (_isPlaying && !ct.IsCancellationRequested)
                            {
                                try
                                {
                                    using var ms = new MemoryStream(frameData);
                                    var bitmap = new BitmapImage();
                                    bitmap.BeginInit();
                                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                    bitmap.StreamSource = ms;
                                    bitmap.EndInit();
                                    bitmap.Freeze();
                                    if (GetSelectedCropMode() == CropMode.SplitLayout)
                                    {
                                        SplitPlaybackImage.Source = bitmap;
                                        SplitPlaybackImage.Visibility = Visibility.Visible;
                                    }
                                    else
                                    {
                                        CroppedPreviewImage.Source = bitmap;
                                        CroppedPreviewImage.Visibility = Visibility.Visible;
                                    }
                                }
                                catch
                                {
                                    // Ignoriere fehlerhafte Frames
                                }
                            }
                        }, System.Windows.Threading.DispatcherPriority.Render, ct);
                    }

                    // Entferne verarbeiteten Frame aus Buffer
                    var remaining = data.Length - frameEnd - 2;
                    frameBuffer = new MemoryStream();
                    if (remaining > 0)
                    {
                        frameBuffer.Write(data, frameEnd + 2, remaining);
                    }

                    data = frameBuffer.ToArray();
                    frameEnd = FindJpegEndMarker(data);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal bei Stop
        }
        catch
        {
            // Ignoriere Fehler
        }
    }

    private static int FindJpegEndMarker(byte[] data)
    {
        // JPEG endet mit FFD9
        for (int i = 0; i < data.Length - 1; i++)
        {
            if (data[i] == 0xFF && data[i + 1] == 0xD9)
            {
                return i;
            }
        }
        return -1;
    }

    private void StopFfmpegPreviewProcess()
    {
        _ffmpegPreviewCts?.Cancel();

        lock (_ffmpegLock)
        {
            if (_ffmpegPreviewProcess is not null)
            {
                try
                {
                    if (!_ffmpegPreviewProcess.HasExited)
                    {
                        _ffmpegPreviewProcess.Kill();
                    }
                    _ffmpegPreviewProcess.Dispose();
                }
                catch
                {
                    // Ignorieren
                }
                _ffmpegPreviewProcess = null;
            }
        }

        _ffmpegPreviewCts?.Dispose();
        _ffmpegPreviewCts = null;
    }

    private readonly record struct SplitFaceAnchor(
        double CenterX,
        double CenterY,
        double FaceWidth,
        double FaceHeight,
        double Score);

    private readonly record struct SplitFaceSample(
        double CenterX,
        double CenterY,
        double FaceWidth,
        double FaceHeight,
        double Confidence)
    {
        public double Weight => (FaceWidth * FaceHeight * 6.0) + Confidence;
    }

    private sealed class SplitFaceClusterAccumulator
    {
        private double _centerX;
        private double _centerY;
        private double _widthSum;
        private double _heightSum;
        private double _confidenceSum;
        private int _count;

        public SplitFaceClusterAccumulator(SplitFaceSample sample)
        {
            _centerX = sample.CenterX;
            _centerY = sample.CenterY;
            _widthSum = sample.FaceWidth;
            _heightSum = sample.FaceHeight;
            _confidenceSum = sample.Confidence;
            _count = 1;
        }

        public void Add(SplitFaceSample sample)
        {
            _count++;
            _centerX = ((_centerX * (_count - 1)) + sample.CenterX) / _count;
            _centerY = ((_centerY * (_count - 1)) + sample.CenterY) / _count;
            _widthSum += sample.FaceWidth;
            _heightSum += sample.FaceHeight;
            _confidenceSum += sample.Confidence;
        }

        public double DistanceTo(double centerX, double centerY)
        {
            var dx = _centerX - centerX;
            var dy = _centerY - centerY;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        public SplitFaceAnchor ToAnchor()
        {
            var avgWidth = _widthSum / _count;
            var avgHeight = _heightSum / _count;
            var avgConfidence = _confidenceSum / _count;
            var score = (_count * 1.6) + (avgWidth * avgHeight * 8.0) + avgConfidence;

            return new SplitFaceAnchor(
                _centerX,
                _centerY,
                avgWidth,
                avgHeight,
                score);
        }
    }

    private enum SplitDragMode
    {
        None = 0,
        Move = 1,
        Resize = 2
    }

    private enum SplitEditorInteractionMode
    {
        Layout = 0,
        Elements = 1
    }

    private enum SplitResizeCorner
    {
        None = 0,
        TopLeft = 1,
        TopRight = 2,
        BottomLeft = 3,
        BottomRight = 4
    }

    private enum SplitRegionTarget
    {
        Primary = 0,
        Secondary = 1
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
