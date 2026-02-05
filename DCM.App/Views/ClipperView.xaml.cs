using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DCM.App.Models;
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
    }

    public event SelectionChangedEventHandler? DraftSelectionChanged;

    public UploadDraft? SelectedDraft => DraftListBox.SelectedItem as UploadDraft;

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
        CandidatesListBox.ItemsSource = list;

        if (list.Count > 0)
        {
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            CandidatesListBox.Visibility = Visibility.Visible;
            LoadingPanel.Visibility = Visibility.Collapsed;
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

        // UI aktualisieren
        PreviewEndTimeText.Text = candidate.DurationFormatted;
        PreviewCurrentTimeText.Text = "0:00";
        PreviewProgressSlider.Value = 0;

        // Clip-Details aktualisieren
        UpdateClipDetails(candidate);

        VideoPlayerPanel.Visibility = Visibility.Visible;
        VideoEmptyStatePanel.Visibility = Visibility.Collapsed;

        try
        {
            // Prüfen ob das Video bereits geladen ist
            if (_isMediaReady && string.Equals(_loadedVideoPath, _currentVideoPath, StringComparison.OrdinalIgnoreCase))
            {
                // Video ist bereits geladen - nur Position ändern
                PreviewMediaElement.Pause();
                PreviewMediaElement.Position = candidate.Start;
            }
            else
            {
                // Video neu laden
                _isMediaReady = false;
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

        // Zur Startposition springen
        PreviewMediaElement.Position = _currentPreviewCandidate.Start;
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

        PreviewMediaElement.Position = _currentPreviewCandidate.Start;
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
        UpdatePlayButtonIcon(false);
    }

    private void StopPlayback()
    {
        _isPlaying = false;
        _previewTimer.Stop();
        PreviewMediaElement.Pause();

        if (_currentPreviewCandidate is not null && _isMediaReady)
        {
            PreviewMediaElement.Position = _currentPreviewCandidate.Start;
        }

        PreviewCurrentTimeText.Text = "0:00";
        PreviewProgressSlider.Value = 0;
        UpdatePlayButtonIcon(false);
    }

    private void StopPreview()
    {
        _isPlaying = false;
        _isMediaReady = false;
        _previewTimer.Stop();
        _currentPreviewCandidate = null;
        _loadedVideoPath = null;

        try
        {
            PreviewMediaElement.Stop();
            PreviewMediaElement.Source = null;
        }
        catch
        {
            // Ignorieren
        }
    }

    private void ShowVideoEmptyState()
    {
        VideoPlayerPanel.Visibility = Visibility.Collapsed;
        VideoEmptyStatePanel.Visibility = Visibility.Visible;
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
