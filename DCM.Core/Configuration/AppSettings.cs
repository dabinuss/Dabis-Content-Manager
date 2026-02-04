using System;
using System.Collections.Generic;
using System.Linq;
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
    public bool SkipSetupDialog { get; set; }

    public AppSettings DeepCopy()
    {
        var persona = Persona ?? new ChannelPersona();
        var llm = Llm ?? new LlmSettings();
        var transcription = Transcription ?? new TranscriptionSettings();

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
            Persona = new ChannelPersona
            {
                Name = persona.Name,
                ChannelName = persona.ChannelName,
                Language = persona.Language,
                ToneOfVoice = persona.ToneOfVoice,
                ContentType = persona.ContentType,
                TargetAudience = persona.TargetAudience,
                AdditionalInstructions = persona.AdditionalInstructions
            },
            Llm = new LlmSettings
            {
                Mode = llm.Mode,
                ModelPreset = llm.ModelPreset,
                LocalModelPath = llm.LocalModelPath,
                ModelType = llm.ModelType,
                SystemPrompt = llm.SystemPrompt,
                MaxTokens = llm.MaxTokens,
                Temperature = llm.Temperature,
                ContextSize = llm.ContextSize,
                TitleCustomPrompt = llm.TitleCustomPrompt,
                DescriptionCustomPrompt = llm.DescriptionCustomPrompt,
                TagsCustomPrompt = llm.TagsCustomPrompt
            },
            Transcription = new TranscriptionSettings
            {
                AutoTranscribeOnVideoSelect = transcription.AutoTranscribeOnVideoSelect,
                ModelSize = transcription.ModelSize,
                Language = transcription.Language
            },
            Language = Language,
            TitleSuggestionCount = TitleSuggestionCount,
            DescriptionSuggestionCount = DescriptionSuggestionCount,
            TagsSuggestionCount = TagsSuggestionCount,
            Theme = Theme,
            RememberDraftsBetweenSessions = RememberDraftsBetweenSessions,
            AutoRemoveCompletedDrafts = AutoRemoveCompletedDrafts,
            SavedDrafts = SavedDrafts?
                .Select(d => new UploadDraftSnapshot
                {
                    Id = d.Id,
                    VideoPath = d.VideoPath,
                    Title = d.Title,
                    Description = d.Description,
                    TagsCsv = d.TagsCsv,
                    ThumbnailPath = d.ThumbnailPath,
                    Transcript = d.Transcript,
                    TranscriptPath = d.TranscriptPath,
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
                    LastUpdated = d.LastUpdated
                }).ToList() ?? new List<UploadDraftSnapshot>(),
            PendingTranscriptionQueue = PendingTranscriptionQueue?.ToList() ?? new List<Guid>(),
            YouTubeOptionsLocale = YouTubeOptionsLocale,
            YouTubeCategoryOptions = YouTubeCategoryOptions?
                .Select(o => new OptionEntry { Code = o.Code, Name = o.Name })
                .ToList() ?? new List<OptionEntry>(),
            YouTubeLanguageOptions = YouTubeLanguageOptions?
                .Select(o => new OptionEntry { Code = o.Code, Name = o.Name })
                .ToList() ?? new List<OptionEntry>(),
            YouTubeLastSyncUtc = YouTubeLastSyncUtc,
            SkipSetupDialog = SkipSetupDialog
        };
    }
}
