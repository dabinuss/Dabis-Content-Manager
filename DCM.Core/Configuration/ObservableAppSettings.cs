using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using DCM.Core.Models;

namespace DCM.Core.Configuration;

/// <summary>
/// Observable-Version der AppSettings mit automatischer Change-Notification.
/// Unterstützt verschachtelte Observable-Objekte und Collections.
/// </summary>
public sealed class ObservableAppSettings : ObservableObject
{
    private string? _lastVideoFolder;
    private string? _defaultVideoFolder;
    private string? _defaultThumbnailFolder;
    private PlatformType _defaultPlatform = PlatformType.YouTube;
    private string? _defaultPlaylistId;
    private string? _defaultSchedulingTime;
    private bool _confirmBeforeUpload;
    private bool _autoConnectYouTube = true;
    private VideoVisibility _defaultVisibility = VideoVisibility.Unlisted;
    private bool _autoApplyDefaultTemplate = true;
    private bool _openBrowserAfterUpload;
    private string? _language;
    private int _titleSuggestionCount = 5;
    private int _descriptionSuggestionCount = 3;
    private int _tagsSuggestionCount = 1;
    private string _theme = "Dark";
    private bool _rememberDraftsBetweenSessions = true;
    private bool _autoRemoveCompletedDrafts = true;
    private string? _youTubeOptionsLocale;
    private DateTime? _youTubeLastSyncUtc;

    public ObservableAppSettings()
    {
        Persona = new ObservableChannelPersona();
        Llm = new ObservableLlmSettings();
        Transcription = new ObservableTranscriptionSettings();
        SavedDrafts = new ObservableCollection<UploadDraftSnapshot>();
        PendingTranscriptionQueue = new ObservableCollection<Guid>();
        YouTubeCategoryOptions = new ObservableCollection<OptionEntry>();
        YouTubeLanguageOptions = new ObservableCollection<OptionEntry>();

        // Verschachtelte PropertyChanged-Events weiterleiten
        Persona.PropertyChanged += NestedObject_PropertyChanged;
        Llm.PropertyChanged += NestedObject_PropertyChanged;
        Transcription.PropertyChanged += NestedObject_PropertyChanged;

        // Collection-Changes weiterleiten
        SavedDrafts.CollectionChanged += Collection_CollectionChanged;
        PendingTranscriptionQueue.CollectionChanged += Collection_CollectionChanged;
        YouTubeCategoryOptions.CollectionChanged += Collection_CollectionChanged;
        YouTubeLanguageOptions.CollectionChanged += Collection_CollectionChanged;
    }

    /// <summary>
    /// Event das ausgelöst wird, wenn irgendein Wert sich ändert (inkl. verschachtelte Objekte).
    /// </summary>
    public event EventHandler? SettingsChanged;

    private void NestedObject_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RaiseSettingsChanged();
    }

    private void Collection_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaiseSettingsChanged();
    }

    protected override bool SetProperty<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (base.SetProperty(ref field, value, propertyName))
        {
            RaiseSettingsChanged();
            return true;
        }
        return false;
    }

    #region Properties

    public string? LastVideoFolder
    {
        get => _lastVideoFolder;
        set => SetProperty(ref _lastVideoFolder, value);
    }

    public string? DefaultVideoFolder
    {
        get => _defaultVideoFolder;
        set => SetProperty(ref _defaultVideoFolder, value);
    }

    public string? DefaultThumbnailFolder
    {
        get => _defaultThumbnailFolder;
        set => SetProperty(ref _defaultThumbnailFolder, value);
    }

    public PlatformType DefaultPlatform
    {
        get => _defaultPlatform;
        set => SetProperty(ref _defaultPlatform, value);
    }

    public string? DefaultPlaylistId
    {
        get => _defaultPlaylistId;
        set => SetProperty(ref _defaultPlaylistId, value);
    }

    public string? DefaultSchedulingTime
    {
        get => _defaultSchedulingTime;
        set => SetProperty(ref _defaultSchedulingTime, value);
    }

    public bool ConfirmBeforeUpload
    {
        get => _confirmBeforeUpload;
        set => SetProperty(ref _confirmBeforeUpload, value);
    }

    public bool AutoConnectYouTube
    {
        get => _autoConnectYouTube;
        set => SetProperty(ref _autoConnectYouTube, value);
    }

    public VideoVisibility DefaultVisibility
    {
        get => _defaultVisibility;
        set => SetProperty(ref _defaultVisibility, value);
    }

    public bool AutoApplyDefaultTemplate
    {
        get => _autoApplyDefaultTemplate;
        set => SetProperty(ref _autoApplyDefaultTemplate, value);
    }

    public bool OpenBrowserAfterUpload
    {
        get => _openBrowserAfterUpload;
        set => SetProperty(ref _openBrowserAfterUpload, value);
    }

    public ObservableChannelPersona Persona { get; }

    public ObservableLlmSettings Llm { get; }

    public ObservableTranscriptionSettings Transcription { get; }

    public string? Language
    {
        get => _language;
        set => SetProperty(ref _language, value);
    }

    public int TitleSuggestionCount
    {
        get => _titleSuggestionCount;
        set => SetProperty(ref _titleSuggestionCount, Math.Clamp(value, 1, 5));
    }

    public int DescriptionSuggestionCount
    {
        get => _descriptionSuggestionCount;
        set => SetProperty(ref _descriptionSuggestionCount, Math.Clamp(value, 1, 5));
    }

    public int TagsSuggestionCount
    {
        get => _tagsSuggestionCount;
        set => SetProperty(ref _tagsSuggestionCount, Math.Clamp(value, 1, 5));
    }

    public string Theme
    {
        get => _theme;
        set => SetProperty(ref _theme, string.IsNullOrWhiteSpace(value) ? "Dark" : value.Trim());
    }

    public bool RememberDraftsBetweenSessions
    {
        get => _rememberDraftsBetweenSessions;
        set => SetProperty(ref _rememberDraftsBetweenSessions, value);
    }

    public bool AutoRemoveCompletedDrafts
    {
        get => _autoRemoveCompletedDrafts;
        set => SetProperty(ref _autoRemoveCompletedDrafts, value);
    }

    public ObservableCollection<UploadDraftSnapshot> SavedDrafts { get; }

    public ObservableCollection<Guid> PendingTranscriptionQueue { get; }

    public string? YouTubeOptionsLocale
    {
        get => _youTubeOptionsLocale;
        set => SetProperty(ref _youTubeOptionsLocale, value);
    }

    public ObservableCollection<OptionEntry> YouTubeCategoryOptions { get; }

    public ObservableCollection<OptionEntry> YouTubeLanguageOptions { get; }

    public DateTime? YouTubeLastSyncUtc
    {
        get => _youTubeLastSyncUtc;
        set => SetProperty(ref _youTubeLastSyncUtc, value);
    }

    #endregion

    /// <summary>
    /// Erstellt eine Kopie als einfache AppSettings (für Serialisierung).
    /// </summary>
    public AppSettings ToAppSettings()
    {
        return new AppSettings
        {
            LastVideoFolder = LastVideoFolder,
            DefaultVideoFolder = DefaultVideoFolder,
            DefaultThumbnailFolder = DefaultThumbnailFolder,
            DefaultPlatform = DefaultPlatform,
            DefaultPlaylistId = DefaultPlaylistId,
            DefaultSchedulingTime = DefaultSchedulingTime,
            ConfirmBeforeUpload = ConfirmBeforeUpload,
            AutoConnectYouTube = AutoConnectYouTube,
            DefaultVisibility = DefaultVisibility,
            AutoApplyDefaultTemplate = AutoApplyDefaultTemplate,
            OpenBrowserAfterUpload = OpenBrowserAfterUpload,
            Persona = Persona.ToChannelPersona(),
            Llm = Llm.ToLlmSettings(),
            Transcription = Transcription.ToTranscriptionSettings(),
            Language = Language,
            TitleSuggestionCount = TitleSuggestionCount,
            DescriptionSuggestionCount = DescriptionSuggestionCount,
            TagsSuggestionCount = TagsSuggestionCount,
            Theme = Theme,
            RememberDraftsBetweenSessions = RememberDraftsBetweenSessions,
            AutoRemoveCompletedDrafts = AutoRemoveCompletedDrafts,
            SavedDrafts = SavedDrafts.Select(d => new UploadDraftSnapshot
            {
                Id = d.Id,
                VideoPath = d.VideoPath,
                Title = d.Title,
                Description = d.Description,
                TagsCsv = d.TagsCsv,
                ThumbnailPath = d.ThumbnailPath,
                Transcript = d.Transcript,
                TranscriptPath = d.TranscriptPath,
                TranscriptSegmentsPath = d.TranscriptSegmentsPath,
                ChaptersText = d.ChaptersText,
                PresetId = d.PresetId,
                VideoResolution = d.VideoResolution,
                VideoFps = d.VideoFps,
                VideoDuration = d.VideoDuration,
                VideoCodec = d.VideoCodec,
                VideoBitrate = d.VideoBitrate,
                AudioInfo = d.AudioInfo,
                AudioBitrate = d.AudioBitrate,
                VideoPreviewPath = d.VideoPreviewPath,
                Platform = d.Platform,
                Visibility = d.Visibility,
                PlaylistId = d.PlaylistId,
                PlaylistTitle = d.PlaylistTitle,
                CategoryId = d.CategoryId,
                Language = d.Language,
                MadeForKids = d.MadeForKids,
                CommentStatus = d.CommentStatus,
                ScheduleEnabled = d.ScheduleEnabled,
                ScheduledDate = d.ScheduledDate,
                ScheduledTimeText = d.ScheduledTimeText,
                UploadState = d.UploadState,
                UploadStatus = d.UploadStatus,
                TranscriptionState = d.TranscriptionState,
                TranscriptionStatus = d.TranscriptionStatus,
                IsGeneratedClipDraft = d.IsGeneratedClipDraft,
                SourceDraftId = d.SourceDraftId,
                LastUpdated = d.LastUpdated
            }).ToList(),
            PendingTranscriptionQueue = PendingTranscriptionQueue.ToList(),
            YouTubeOptionsLocale = YouTubeOptionsLocale,
            YouTubeCategoryOptions = YouTubeCategoryOptions.Select(o => new OptionEntry
            {
                Code = o.Code,
                Name = o.Name
            }).ToList(),
            YouTubeLanguageOptions = YouTubeLanguageOptions.Select(o => new OptionEntry
            {
                Code = o.Code,
                Name = o.Name
            }).ToList(),
            YouTubeLastSyncUtc = YouTubeLastSyncUtc
        };
    }

    /// <summary>
    /// Lädt Werte aus einfachen AppSettings.
    /// Löst keine Change-Events während des Ladens aus.
    /// </summary>
    public void LoadFrom(AppSettings? source)
    {
        var data = source ?? new AppSettings();

        // Temporär Events deaktivieren
        _isLoading = true;

        try
        {
            _lastVideoFolder = data.LastVideoFolder;
            _defaultVideoFolder = data.DefaultVideoFolder;
            _defaultThumbnailFolder = data.DefaultThumbnailFolder;
            _defaultPlatform = data.DefaultPlatform;
            _defaultPlaylistId = data.DefaultPlaylistId;
            _defaultSchedulingTime = data.DefaultSchedulingTime;
            _confirmBeforeUpload = data.ConfirmBeforeUpload;
            _autoConnectYouTube = data.AutoConnectYouTube;
            _defaultVisibility = data.DefaultVisibility;
            _autoApplyDefaultTemplate = data.AutoApplyDefaultTemplate;
            _openBrowserAfterUpload = data.OpenBrowserAfterUpload;
            _language = data.Language;
            _titleSuggestionCount = Math.Clamp(data.TitleSuggestionCount, 1, 5);
            _descriptionSuggestionCount = Math.Clamp(data.DescriptionSuggestionCount, 1, 5);
            _tagsSuggestionCount = Math.Clamp(data.TagsSuggestionCount, 1, 5);
            _theme = string.IsNullOrWhiteSpace(data.Theme) ? "Dark" : data.Theme.Trim();
            _rememberDraftsBetweenSessions = data.RememberDraftsBetweenSessions;
            _autoRemoveCompletedDrafts = data.AutoRemoveCompletedDrafts;
            _youTubeOptionsLocale = data.YouTubeOptionsLocale;
            _youTubeLastSyncUtc = data.YouTubeLastSyncUtc;

            Persona.LoadFrom(data.Persona);
            Llm.LoadFrom(data.Llm);
            Transcription.LoadFrom(data.Transcription);

            SavedDrafts.Clear();
            if (data.SavedDrafts is not null)
            {
                foreach (var draft in data.SavedDrafts)
                {
                    SavedDrafts.Add(draft);
                }
            }

            PendingTranscriptionQueue.Clear();
            if (data.PendingTranscriptionQueue is not null)
            {
                foreach (var id in data.PendingTranscriptionQueue)
                {
                    PendingTranscriptionQueue.Add(id);
                }
            }

            YouTubeCategoryOptions.Clear();
            if (data.YouTubeCategoryOptions is not null)
            {
                foreach (var option in data.YouTubeCategoryOptions)
                {
                    YouTubeCategoryOptions.Add(option);
                }
            }

            YouTubeLanguageOptions.Clear();
            if (data.YouTubeLanguageOptions is not null)
            {
                foreach (var option in data.YouTubeLanguageOptions)
                {
                    YouTubeLanguageOptions.Add(option);
                }
            }
        }
        finally
        {
            _isLoading = false;
        }
    }

    private bool _isLoading;

    /// <summary>
    /// Führt eine Aktion aus, ohne dabei SettingsChanged-Events auszulösen.
    /// Nützlich für Batch-Updates.
    /// </summary>
    public void SuppressChanges(Action action)
    {
        var wasLoading = _isLoading;
        _isLoading = true;
        try
        {
            action();
        }
        finally
        {
            _isLoading = wasLoading;
        }
    }

    private void RaiseSettingsChanged()
    {
        if (!_isLoading)
        {
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
