using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DCM.App.Models;
using DCM.Core.Models;

namespace DCM.App.Services;

internal sealed class DraftRepository
{
    private readonly DraftTranscriptStore _transcriptStore;

    public DraftRepository(DraftTranscriptStore? transcriptStore = null)
    {
        _transcriptStore = transcriptStore ?? new DraftTranscriptStore();
    }

    internal sealed record DraftLoadResult(
        IReadOnlyList<UploadDraft> Drafts,
        bool RemovedDuringRestore,
        bool MigratedTranscripts);

    public DraftLoadResult LoadDrafts(
        IEnumerable<UploadDraftSnapshot> snapshots,
        bool autoRemoveCompleted,
        Func<UploadDraft, bool> shouldAutoRemove)
    {
        if (snapshots is null)
        {
            return new DraftLoadResult(
                Array.Empty<UploadDraft>(),
                RemovedDuringRestore: false,
                MigratedTranscripts: false);
        }

        var drafts = new List<UploadDraft>();
        var removedDuringRestore = false;
        var migratedTranscripts = false;

        foreach (var snapshot in snapshots)
        {
            var draft = UploadDraft.FromSnapshot(snapshot);
            var transcriptLoaded = false;

            if (!string.IsNullOrWhiteSpace(snapshot.TranscriptPath))
            {
                var storedTranscript = _transcriptStore.LoadTranscript(draft.Id, snapshot.TranscriptPath);
                if (!string.IsNullOrWhiteSpace(storedTranscript))
                {
                    draft.SetTranscriptFromStorage(storedTranscript);
                    transcriptLoaded = true;
                }
            }

            if (!transcriptLoaded && !string.IsNullOrWhiteSpace(snapshot.Transcript))
            {
                draft.SetTranscriptFromStorage(snapshot.Transcript);

                var migratedPath = _transcriptStore.SaveTranscript(draft.Id, snapshot.Transcript);
                if (!string.IsNullOrWhiteSpace(migratedPath))
                {
                    snapshot.TranscriptPath = migratedPath;
                    snapshot.Transcript = null;
                    migratedTranscripts = true;
                }
            }

            if (autoRemoveCompleted && shouldAutoRemove(draft))
            {
                removedDuringRestore = true;
                continue;
            }

            drafts.Add(draft);
        }

        return new DraftLoadResult(drafts, removedDuringRestore, migratedTranscripts);
    }

    public List<UploadDraftSnapshot> CreateSnapshots(IEnumerable<UploadDraft> drafts)
    {
        if (drafts is null)
        {
            return new List<UploadDraftSnapshot>();
        }

        var snapshots = new List<UploadDraftSnapshot>();

        foreach (var draft in drafts)
        {
            var snapshot = draft.ToSnapshot();
            var transcript = draft.Transcript;
            var hasTranscript = !string.IsNullOrWhiteSpace(transcript);
            var expectedPath = hasTranscript ? _transcriptStore.GetTranscriptPath(draft.Id) : null;
            var needsWrite = draft.TranscriptDirty
                             || (hasTranscript && expectedPath is not null && !File.Exists(expectedPath));

            if (needsWrite)
            {
                var transcriptPath = _transcriptStore.SaveTranscript(draft.Id, transcript);

                if (!string.IsNullOrWhiteSpace(transcriptPath))
                {
                    snapshot.TranscriptPath = transcriptPath;
                    snapshot.Transcript = null;
                }
                else
                {
                    snapshot.TranscriptPath = null;
                    snapshot.Transcript = null;
                }

                if (string.IsNullOrWhiteSpace(transcript) || !string.IsNullOrWhiteSpace(transcriptPath))
                {
                    draft.MarkTranscriptPersisted();
                }
            }
            else if (hasTranscript && expectedPath is not null)
            {
                snapshot.TranscriptPath = expectedPath;
                snapshot.Transcript = null;
            }

            snapshots.Add(snapshot);
        }

        return snapshots;
    }
}
