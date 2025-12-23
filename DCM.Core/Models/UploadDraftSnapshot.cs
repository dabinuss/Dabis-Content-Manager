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
    public string? UploadState { get; set; }
    public string? UploadStatus { get; set; }
    public string? TranscriptionState { get; set; }
    public string? TranscriptionStatus { get; set; }
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
}
