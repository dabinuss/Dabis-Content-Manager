using System;

namespace DCM.Core.Models;

public sealed class UploadDraftSnapshot
{
    public Guid Id { get; set; }
    public string? VideoPath { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? TagsCsv { get; set; }
    public string? ThumbnailPath { get; set; }
    public string? Transcript { get; set; }
    public string? PresetId { get; set; }
    public string? VideoResolution { get; set; }
    public string? VideoFps { get; set; }
    public string? VideoDuration { get; set; }
    public string? VideoCodec { get; set; }
    public string? VideoBitrate { get; set; }
    public string? AudioInfo { get; set; }
    public string? AudioBitrate { get; set; }
    public string? VideoPreviewPath { get; set; }
    public string? Platform { get; set; }
    public string? Visibility { get; set; }
    public string? PlaylistId { get; set; }
    public string? PlaylistTitle { get; set; }
    public string? CategoryId { get; set; }
    public string? Language { get; set; }
    public string? MadeForKids { get; set; }
    public string? CommentStatus { get; set; }
    public bool ScheduleEnabled { get; set; }
    public DateTime? ScheduledDate { get; set; }
    public string? ScheduledTimeText { get; set; }
    public string? UploadState { get; set; }
    public string? UploadStatus { get; set; }
    public string? TranscriptionState { get; set; }
    public string? TranscriptionStatus { get; set; }
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
}
