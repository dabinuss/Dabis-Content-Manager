using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using DCM.Core.Models;

namespace DCM.App.Models;

public sealed class UploadDraft : INotifyPropertyChanged
{
    public Guid Id { get; } = Guid.NewGuid();

    private string _videoPath = string.Empty;
    private string _title = string.Empty;
    private string _description = string.Empty;
    private string _tagsCsv = string.Empty;
    private string _thumbnailPath = string.Empty;
    private string _transcript = string.Empty;
    private string _fileName = string.Empty;
    private long _fileSizeBytes;
    private UploadDraftUploadState _uploadState = UploadDraftUploadState.Pending;
    private string? _uploadStatus;
    private UploadDraftTranscriptionState _transcriptionState = UploadDraftTranscriptionState.None;
    private string? _transcriptionStatus;
    private double _uploadProgress;
    private bool _uploadProgressIsIndeterminate = true;
    private double _transcriptionProgress;
    private bool _transcriptionProgressIsIndeterminate = true;
    private DateTimeOffset _lastUpdated = DateTimeOffset.UtcNow;

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

    public string ThumbnailPath
    {
        get => _thumbnailPath;
        set => SetProperty(ref _thumbnailPath, value);
    }

    public string Transcript
    {
        get => _transcript;
        set => SetProperty(ref _transcript, value);
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
            return;
        }

        try
        {
            var info = new FileInfo(videoPath);
            FileName = info.Name;
            FileSizeBytes = info.Exists ? info.Length : 0;

            if (string.IsNullOrWhiteSpace(Title))
            {
                Title = Path.GetFileNameWithoutExtension(info.Name) ?? Title;
            }
        }
        catch
        {
            FileName = Path.GetFileName(videoPath) ?? videoPath;
            FileSizeBytes = 0;
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
            VideoPath = VideoPath,
            Title = Title,
            Description = Description,
            TagsCsv = TagsCsv,
            ThumbnailPath = ThumbnailPath,
            Transcript = Transcript,
            UploadState = UploadState.ToString(),
            UploadStatus = UploadStatus,
            TranscriptionState = TranscriptionState.ToString(),
            TranscriptionStatus = TranscriptionStatus,
            LastUpdated = _lastUpdated
        };
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
            Transcript = snapshot.Transcript ?? string.Empty
        };

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
    Failed
}
