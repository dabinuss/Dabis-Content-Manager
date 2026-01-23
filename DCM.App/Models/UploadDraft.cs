using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using DCM.Core.Models;

namespace DCM.App.Models;

public sealed class UploadDraft : INotifyPropertyChanged
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    private string _videoPath = string.Empty;
    private string _title = string.Empty;
    private string _description = string.Empty;
    private string _tagsCsv = string.Empty;
    private string _thumbnailPath = string.Empty;
    private string _transcript = string.Empty;
    private string _chaptersText = string.Empty;
    private string? _presetId;
    private string _fileName = string.Empty;
    private long _fileSizeBytes;
    private string? _videoResolution;
    private string? _videoFps;
    private string? _videoDuration;
    private string? _videoCodec;
    private string? _videoBitrate;
    private string? _audioInfo;
    private string? _audioBitrate;
    private string? _videoPreviewPath;
    private PlatformType _platform = PlatformType.YouTube;
    private VideoVisibility _visibility = VideoVisibility.Unlisted;
    private string? _playlistId;
    private string? _playlistTitle;
    private string? _categoryId;
    private string? _language;
    private MadeForKidsSetting _madeForKids = MadeForKidsSetting.Default;
    private CommentStatusSetting _commentStatus = CommentStatusSetting.Default;
    private bool _scheduleEnabled;
    private DateTime? _scheduledDate;
    private string? _scheduledTimeText;
    private UploadDraftUploadState _uploadState = UploadDraftUploadState.Pending;
    private string? _uploadStatus;
    private UploadDraftTranscriptionState _transcriptionState = UploadDraftTranscriptionState.None;
    private string? _transcriptionStatus;
    private double _uploadProgress;
    private bool _uploadProgressIsIndeterminate = true;
    private double _transcriptionProgress;
    private bool _transcriptionProgressIsIndeterminate = true;
    private DateTimeOffset _lastUpdated = DateTimeOffset.UtcNow;
    private bool _transcriptDirty;
    private bool _suppressTranscriptDirty;

    public string VideoPath
    {
        get => _videoPath;
        set
        {
            if (SetProperty(ref _videoPath, value))
            {
                UpdateFileInfo(value);
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(HasVideo));
            }
        }
    }

    public string Title
    {
        get => _title;
        set
        {
            if (SetProperty(ref _title, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public string TagsCsv
    {
        get => _tagsCsv;
        set => SetProperty(ref _tagsCsv, value);
    }

    public string? PresetId
    {
        get => _presetId;
        set => SetProperty(ref _presetId, value);
    }

    public string ThumbnailPath
    {
        get => _thumbnailPath;
        set => SetProperty(ref _thumbnailPath, value);
    }

    public string Transcript
    {
        get => _transcript;
        set
        {
            if (SetProperty(ref _transcript, value))
            {
                if (!_suppressTranscriptDirty)
                {
                    _transcriptDirty = true;
                }
            }
        }
    }

    public string ChaptersText
    {
        get => _chaptersText;
        set => SetProperty(ref _chaptersText, value);
    }

    public PlatformType Platform
    {
        get => _platform;
        set => SetProperty(ref _platform, value);
    }

    public VideoVisibility Visibility
    {
        get => _visibility;
        set => SetProperty(ref _visibility, value);
    }

    public string? PlaylistId
    {
        get => _playlistId;
        set => SetProperty(ref _playlistId, value);
    }

    public string? PlaylistTitle
    {
        get => _playlistTitle;
        set => SetProperty(ref _playlistTitle, value);
    }

    public string? CategoryId
    {
        get => _categoryId;
        set => SetProperty(ref _categoryId, value);
    }

    public string? Language
    {
        get => _language;
        set => SetProperty(ref _language, value);
    }

    public MadeForKidsSetting MadeForKids
    {
        get => _madeForKids;
        set => SetProperty(ref _madeForKids, value);
    }

    public CommentStatusSetting CommentStatus
    {
        get => _commentStatus;
        set => SetProperty(ref _commentStatus, value);
    }

    public bool ScheduleEnabled
    {
        get => _scheduleEnabled;
        set => SetProperty(ref _scheduleEnabled, value);
    }

    public DateTime? ScheduledDate
    {
        get => _scheduledDate;
        set => SetProperty(ref _scheduledDate, value);
    }

    public string? ScheduledTimeText
    {
        get => _scheduledTimeText;
        set => SetProperty(ref _scheduledTimeText, value);
    }

    public string FileName
    {
        get => _fileName;
        private set => SetProperty(ref _fileName, value);
    }

    public long FileSizeBytes
    {
        get => _fileSizeBytes;
        private set
        {
            if (SetProperty(ref _fileSizeBytes, value))
            {
                OnPropertyChanged(nameof(FileSizeDisplay));
            }
        }
    }

    public string FileSizeDisplay => FormatFileSize(_fileSizeBytes);

    public string? VideoResolution
    {
        get => _videoResolution;
        set => SetProperty(ref _videoResolution, value);
    }

    public string? VideoFps
    {
        get => _videoFps;
        set => SetProperty(ref _videoFps, value);
    }

    public string? VideoDuration
    {
        get => _videoDuration;
        set => SetProperty(ref _videoDuration, value);
    }

    public string? VideoCodec
    {
        get => _videoCodec;
        set => SetProperty(ref _videoCodec, value);
    }

    public string? VideoBitrate
    {
        get => _videoBitrate;
        set => SetProperty(ref _videoBitrate, value);
    }

    public string? AudioInfo
    {
        get => _audioInfo;
        set => SetProperty(ref _audioInfo, value);
    }

    public string? AudioBitrate
    {
        get => _audioBitrate;
        set => SetProperty(ref _audioBitrate, value);
    }

    public string? VideoPreviewPath
    {
        get => _videoPreviewPath;
        set => SetProperty(ref _videoPreviewPath, value);
    }

    public UploadDraftUploadState UploadState
    {
        get => _uploadState;
        set => SetProperty(ref _uploadState, value);
    }

    public string? UploadStatus
    {
        get => _uploadStatus;
        set => SetProperty(ref _uploadStatus, value);
    }

    public UploadDraftTranscriptionState TranscriptionState
    {
        get => _transcriptionState;
        set => SetProperty(ref _transcriptionState, value);
    }

    public string? TranscriptionStatus
    {
        get => _transcriptionStatus;
        set => SetProperty(ref _transcriptionStatus, value);
    }

    public double UploadProgress
    {
        get => _uploadProgress;
        set => SetProperty(ref _uploadProgress, value);
    }

    public bool IsUploadProgressIndeterminate
    {
        get => _uploadProgressIsIndeterminate;
        set => SetProperty(ref _uploadProgressIsIndeterminate, value);
    }

    public double TranscriptionProgress
    {
        get => _transcriptionProgress;
        set => SetProperty(ref _transcriptionProgress, value);
    }

    public bool IsTranscriptionProgressIndeterminate
    {
        get => _transcriptionProgressIsIndeterminate;
        set => SetProperty(ref _transcriptionProgressIsIndeterminate, value);
    }

    public DateTimeOffset LastUpdated => _lastUpdated;

    public bool TranscriptDirty => _transcriptDirty;

    public bool HasVideo => !string.IsNullOrWhiteSpace(VideoPath);

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title))
            {
                return Title.Trim();
            }

            if (!string.IsNullOrWhiteSpace(FileName))
            {
                return FileName;
            }

            if (!string.IsNullOrWhiteSpace(VideoPath))
            {
                return Path.GetFileName(VideoPath) ?? "(Video)";
            }

            return "(neu)";
        }
    }

    private void UpdateFileInfo(string? videoPath)
    {
        if (string.IsNullOrWhiteSpace(videoPath))
        {
            FileName = string.Empty;
            FileSizeBytes = 0;
            VideoResolution = null;
            VideoFps = null;
            VideoDuration = null;
            VideoCodec = null;
            VideoBitrate = null;
            AudioInfo = null;
            AudioBitrate = null;
            VideoPreviewPath = null;
            return;
        }

        try
        {
            var info = new FileInfo(videoPath);
            FileName = info.Name;
            FileSizeBytes = info.Exists ? info.Length : 0;
            VideoResolution = null;
            VideoFps = null;
            VideoDuration = null;
            VideoCodec = null;
            VideoBitrate = null;
            AudioInfo = null;
            AudioBitrate = null;
            VideoPreviewPath = null;

            if (string.IsNullOrWhiteSpace(Title))
            {
                Title = Path.GetFileNameWithoutExtension(info.Name) ?? Title;
            }
        }
        catch
        {
            FileName = Path.GetFileName(videoPath) ?? videoPath;
            FileSizeBytes = 0;
            VideoResolution = null;
            VideoFps = null;
            VideoDuration = null;
            VideoCodec = null;
            VideoBitrate = null;
            AudioInfo = null;
            AudioBitrate = null;
            VideoPreviewPath = null;
        }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] sizes = { "B", "KB", "MB", "GB" };
        var order = 0;
        double len = bytes;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }

    public UploadDraftSnapshot ToSnapshot()
    {
        return new UploadDraftSnapshot
        {
            Id = Id,
            VideoPath = VideoPath,
            Title = Title,
            Description = Description,
            TagsCsv = TagsCsv,
            ThumbnailPath = ThumbnailPath,
            Transcript = Transcript,
            ChaptersText = ChaptersText,
            PresetId = PresetId,
            VideoResolution = VideoResolution,
            VideoFps = VideoFps,
            VideoDuration = VideoDuration,
            VideoCodec = VideoCodec,
            VideoBitrate = VideoBitrate,
            AudioInfo = AudioInfo,
            AudioBitrate = AudioBitrate,
            VideoPreviewPath = VideoPreviewPath,
            Platform = Platform.ToString(),
            Visibility = Visibility.ToString(),
            PlaylistId = PlaylistId,
            PlaylistTitle = PlaylistTitle,
            CategoryId = CategoryId,
            Language = Language,
            MadeForKids = MadeForKids.ToString(),
            CommentStatus = CommentStatus.ToString(),
            ScheduleEnabled = ScheduleEnabled,
            ScheduledDate = ScheduledDate,
            ScheduledTimeText = ScheduledTimeText,
            UploadState = UploadState.ToString(),
            UploadStatus = UploadStatus,
            TranscriptionState = TranscriptionState.ToString(),
            TranscriptionStatus = TranscriptionStatus,
            LastUpdated = _lastUpdated
        };
    }

    internal void SetTranscriptFromStorage(string? value)
    {
        _suppressTranscriptDirty = true;
        Transcript = value ?? string.Empty;
        _suppressTranscriptDirty = false;
        _transcriptDirty = false;
    }

    internal void MarkTranscriptPersisted()
    {
        _transcriptDirty = false;
    }

    public static UploadDraft FromSnapshot(UploadDraftSnapshot snapshot)
    {
        var draft = new UploadDraft
        {
            VideoPath = snapshot.VideoPath ?? string.Empty,
            Title = snapshot.Title ?? string.Empty,
            Description = snapshot.Description ?? string.Empty,
            TagsCsv = snapshot.TagsCsv ?? string.Empty,
            ThumbnailPath = snapshot.ThumbnailPath ?? string.Empty,
            ChaptersText = snapshot.ChaptersText ?? string.Empty,
            PresetId = snapshot.PresetId,
            VideoResolution = snapshot.VideoResolution,
            VideoFps = snapshot.VideoFps,
            VideoDuration = snapshot.VideoDuration,
            VideoCodec = snapshot.VideoCodec,
            VideoBitrate = snapshot.VideoBitrate,
            AudioInfo = snapshot.AudioInfo,
            AudioBitrate = snapshot.AudioBitrate,
            VideoPreviewPath = snapshot.VideoPreviewPath,
            PlaylistId = snapshot.PlaylistId,
            PlaylistTitle = snapshot.PlaylistTitle,
            CategoryId = snapshot.CategoryId,
            Language = snapshot.Language,
            ScheduleEnabled = snapshot.ScheduleEnabled,
            ScheduledDate = snapshot.ScheduledDate,
            ScheduledTimeText = snapshot.ScheduledTimeText
        };

        draft.Id = snapshot.Id == Guid.Empty ? Guid.NewGuid() : snapshot.Id;

        if (Enum.TryParse(snapshot.Platform, out PlatformType platform))
        {
            draft.Platform = platform;
        }

        if (Enum.TryParse(snapshot.Visibility, out VideoVisibility visibility))
        {
            draft.Visibility = visibility;
        }

        if (Enum.TryParse(snapshot.MadeForKids, out MadeForKidsSetting madeForKids))
        {
            draft.MadeForKids = madeForKids;
        }

        if (Enum.TryParse(snapshot.CommentStatus, out CommentStatusSetting commentStatus))
        {
            draft.CommentStatus = commentStatus;
        }

        if (Enum.TryParse(snapshot.UploadState, out UploadDraftUploadState uploadState))
        {
            draft.UploadState = uploadState;
        }

        if (Enum.TryParse(snapshot.TranscriptionState, out UploadDraftTranscriptionState transcriptionState))
        {
            draft.TranscriptionState = transcriptionState;
        }

        draft.UploadStatus = snapshot.UploadStatus;
        draft.TranscriptionStatus = snapshot.TranscriptionStatus;
        draft._lastUpdated = snapshot.LastUpdated;

        if (!string.IsNullOrWhiteSpace(snapshot.Transcript))
        {
            draft.SetTranscriptFromStorage(snapshot.Transcript);
        }

        return draft;
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        Touch();
        return true;
    }

    private void Touch()
    {
        _lastUpdated = DateTimeOffset.UtcNow;
        OnPropertyChanged(nameof(LastUpdated));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public enum UploadDraftUploadState
{
    Pending,
    Uploading,
    Completed,
    Failed
}

public enum UploadDraftTranscriptionState
{
    None,
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}
