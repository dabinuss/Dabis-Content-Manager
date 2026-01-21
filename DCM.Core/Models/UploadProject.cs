// DCM.Core/Models/UploadProject.cs

using System;
using System.Collections.Generic;
using System.Linq;

namespace DCM.Core.Models;

public sealed class UploadProject
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public PlatformType Platform { get; set; } = PlatformType.YouTube;

    public string VideoFilePath { get; set; } = string.Empty;

    /// <summary>
    /// Optionaler Pfad zu einer Thumbnail-Datei (PNG/JPG).
    /// </summary>
    public string? ThumbnailPath { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string? PlaylistTitle { get; set; }

    /// <summary>
    /// Optionaler Volltext-Transkriptinhalt (z. B. von externer Transkript-Engine).
    /// </summary>
    public string? TranscriptText { get; set; }

    /// <summary>
    /// Optionaler Kapiteltext fuer Description-Templates.
    /// </summary>
    public string? ChaptersText { get; set; }

    public List<string> Tags { get; } = new();

    public string? CategoryId { get; set; }

    public string? Language { get; set; }

    public bool? MadeForKids { get; set; }

    public CommentStatusSetting CommentStatus { get; set; } = CommentStatusSetting.Default;

    public VideoVisibility Visibility { get; set; } = VideoVisibility.Unlisted;

    public string? PlaylistId { get; set; }

    public DateTimeOffset? ScheduledTime { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    public void SetTagsFromCsv(string csv)
    {
        Tags.Clear();

        if (string.IsNullOrWhiteSpace(csv))
        {
            return;
        }

        var parts = csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !string.IsNullOrWhiteSpace(p));

        foreach (var tag in parts)
        {
            Tags.Add(tag);
        }
    }

    public string GetTagsAsCsv() => string.Join(", ", Tags);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(VideoFilePath))
        {
            throw new InvalidOperationException("Video file path is required.");
        }

        if (string.IsNullOrWhiteSpace(Title))
        {
            throw new InvalidOperationException("Title is required.");
        }

        // Plattform-Check für zukünftige Erweiterungen
        if (!Enum.IsDefined(typeof(PlatformType), Platform))
        {
            throw new InvalidOperationException($"Unsupported platform: {Platform}");
        }

        if (ScheduledTime is { } scheduled && scheduled < DateTimeOffset.Now.AddMinutes(-1))
        {
            // Nur milder Check: „deutlich in der Vergangenheit“ vermeiden
            throw new InvalidOperationException("Scheduled time must not be in the past.");
        }

        // ThumbnailPath wird nur im UI milde geprüft (Warnung), kein Hard-Fail hier.
    }
}
