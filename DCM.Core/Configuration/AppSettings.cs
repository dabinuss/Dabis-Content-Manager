using System;
using System.Collections.Generic;
using DCM.Core.Models;

namespace DCM.Core.Configuration;

public sealed class AppSettings
{
    public string? LastVideoFolder { get; set; }
    public string? DefaultVideoFolder { get; set; }
    public string? DefaultThumbnailFolder { get; set; }
    public PlatformType DefaultPlatform { get; set; } = PlatformType.YouTube;
    public string? DefaultPlaylistId { get; set; }
    public string? DefaultSchedulingTime { get; set; }
    public bool ConfirmBeforeUpload { get; set; } = false;
    public bool AutoConnectYouTube { get; set; } = true;
    public VideoVisibility DefaultVisibility { get; set; } = VideoVisibility.Unlisted;
    public bool AutoApplyDefaultTemplate { get; set; } = true;
    public bool OpenBrowserAfterUpload { get; set; } = false;
    public ChannelPersona Persona { get; set; } = new();
    public LlmSettings Llm { get; set; } = new();
    public TranscriptionSettings Transcription { get; set; } = new();
    public string? Language { get; set; }
    public int TitleSuggestionCount { get; set; } = 5;
    public int DescriptionSuggestionCount { get; set; } = 3;
    public int TagsSuggestionCount { get; set; } = 1;
    public string Theme { get; set; } = "Dark";
    public bool RememberDraftsBetweenSessions { get; set; } = true;
    public bool AutoRemoveCompletedDrafts { get; set; } = true;
    public List<UploadDraftSnapshot> SavedDrafts { get; set; } = new();
    public List<Guid> PendingTranscriptionQueue { get; set; } = new();
    public string? YouTubeOptionsLocale { get; set; }
    public List<OptionEntry> YouTubeCategoryOptions { get; set; } = new();
    public List<OptionEntry> YouTubeLanguageOptions { get; set; } = new();
    public DateTime? YouTubeLastSyncUtc { get; set; }
}
