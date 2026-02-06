using System;
using System.Collections.Generic;
using System.Linq;
using DCM.App.Infrastructure;
using DCM.App.Models;
using DCM.Core.Logging;
using DCM.Core.Models;
using DCM.Transcription.PostProcessing;

namespace DCM.App.Services;

/// <summary>
/// Erstellt Upload-Drafts aus gerenderten Clips.
/// </summary>
public sealed class ClipToDraftConverter
{
    private readonly DraftTranscriptStore _segmentStore;
    private readonly IAppLogger _logger;

    public ClipToDraftConverter() : this(null, null)
    {
    }

    internal ClipToDraftConverter(DraftTranscriptStore? segmentStore = null, IAppLogger? logger = null)
    {
        _segmentStore = segmentStore ?? new DraftTranscriptStore();
        _logger = logger ?? AppLogger.Instance;
    }

    public UploadDraft CreateDraftFromClip(
        ClipRenderJob job,
        ClipRenderResult result,
        IReadOnlyList<TranscriptionSegment> sourceSegments)
    {
        if (job is null)
        {
            throw new ArgumentNullException(nameof(job));
        }

        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        if (!result.Success || string.IsNullOrWhiteSpace(result.OutputPath))
        {
            throw new InvalidOperationException("Clip-Rendering war nicht erfolgreich.");
        }

        var draft = new UploadDraft
        {
            VideoPath = result.OutputPath,
            Title = GenerateTitle(job.Candidate),
            Description = string.Empty,
            TagsCsv = string.Empty,
            Platform = PlatformType.YouTube,
            Visibility = VideoVisibility.Private,
            UploadStatus = LocalizationHelper.Get("Upload.Status.Ready"),
            TranscriptionState = UploadDraftTranscriptionState.Completed,
            TranscriptionStatus = LocalizationHelper.Format("Status.Transcription.Completed", job.Duration.TotalSeconds)
        };

        var transcript = ExtractClipTranscript(sourceSegments, job.StartTime, job.EndTime);
        if (!string.IsNullOrWhiteSpace(transcript))
        {
            draft.Transcript = transcript;
        }

        var clipSegments = ExtractClipSegments(sourceSegments, job.StartTime, job.EndTime);
        if (clipSegments.Count > 0)
        {
            var path = _segmentStore.SaveSegments(draft.Id, clipSegments);
            draft.TranscriptSegmentsPath = path;
        }

        return draft;
    }

    private static string GenerateTitle(ClipCandidate? candidate)
    {
        if (candidate is null)
        {
            return "Clip";
        }

        if (!string.IsNullOrWhiteSpace(candidate.Reason))
        {
            var title = candidate.Reason.Split(',', '.')[0].Trim();
            if (title.Length <= 100)
            {
                return title;
            }
        }

        if (!string.IsNullOrWhiteSpace(candidate.PreviewText))
        {
            var words = candidate.PreviewText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(8);
            return string.Join(" ", words) + "...";
        }

        return "Clip";
    }

    private static string ExtractClipTranscript(
        IReadOnlyList<TranscriptionSegment> segments,
        TimeSpan clipStart,
        TimeSpan clipEnd)
    {
        if (segments is null || segments.Count == 0)
        {
            return string.Empty;
        }

        var texts = new List<string>();

        foreach (var segment in segments)
        {
            if (segment.End <= clipStart || segment.Start >= clipEnd)
            {
                continue;
            }

            var wordText = BuildTextFromWords(segment.Words, clipStart, clipEnd);
            if (!string.IsNullOrWhiteSpace(wordText))
            {
                texts.Add(wordText);
                continue;
            }

            if (segment.Words is not null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(segment.Text))
            {
                texts.Add(segment.Text.Trim());
            }
        }

        return string.Join(" ", texts);
    }

    private static List<TranscriptionSegment> ExtractClipSegments(
        IReadOnlyList<TranscriptionSegment> segments,
        TimeSpan clipStart,
        TimeSpan clipEnd)
    {
        var result = new List<TranscriptionSegment>();

        if (segments is null || segments.Count == 0)
        {
            return result;
        }

        foreach (var segment in segments)
        {
            var start = segment.Start < clipStart ? clipStart : segment.Start;
            var end = segment.End > clipEnd ? clipEnd : segment.End;
            if (end <= start)
            {
                continue;
            }

            List<TranscriptionWord>? words = null;
            string? text = null;

            if (segment.Words is not null && segment.Words.Count > 0)
            {
                words = new List<TranscriptionWord>();
                var wordTexts = new List<string>();

                foreach (var word in segment.Words)
                {
                    if (word.End <= clipStart || word.Start >= clipEnd)
                    {
                        continue;
                    }

                    var wordStart = word.Start < clipStart ? clipStart : word.Start;
                    var wordEnd = word.End > clipEnd ? clipEnd : word.End;
                    if (wordEnd <= wordStart)
                    {
                        continue;
                    }

                    words.Add(new TranscriptionWord
                    {
                        Text = word.Text,
                        Start = wordStart - clipStart,
                        End = wordEnd - clipStart,
                        Probability = word.Probability
                    });
                    if (!string.IsNullOrWhiteSpace(word.Text))
                    {
                        wordTexts.Add(word.Text);
                    }
                }

                if (words.Count == 0)
                {
                    continue;
                }

                text = wordTexts.Count > 0 ? string.Join(" ", wordTexts) : segment.Text;
            }
            else
            {
                text = segment.Text;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            result.Add(new TranscriptionSegment
            {
                Text = text,
                Start = start - clipStart,
                End = end - clipStart,
                Words = words
            });
        }

        return result;
    }

    private static string? BuildTextFromWords(
        IReadOnlyList<TranscriptionWord>? words,
        TimeSpan clipStart,
        TimeSpan clipEnd)
    {
        if (words is null || words.Count == 0)
        {
            return null;
        }

        var parts = words
            .Where(w => w.End > clipStart && w.Start < clipEnd)
            .Select(w => w.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }
}
