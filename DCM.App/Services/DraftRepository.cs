using System;
using System.Collections.Generic;
using System.Linq;
using DCM.App.Models;
using DCM.Core.Models;

namespace DCM.App.Services;

internal sealed class DraftRepository
{
    internal sealed record DraftLoadResult(
        IReadOnlyList<UploadDraft> Drafts,
        bool RemovedDuringRestore);

    public DraftLoadResult LoadDrafts(
        IEnumerable<UploadDraftSnapshot> snapshots,
        bool autoRemoveCompleted,
        Func<UploadDraft, bool> shouldAutoRemove)
    {
        if (snapshots is null)
        {
            return new DraftLoadResult(Array.Empty<UploadDraft>(), RemovedDuringRestore: false);
        }

        var drafts = new List<UploadDraft>();
        var removedDuringRestore = false;

        foreach (var snapshot in snapshots)
        {
            var draft = UploadDraft.FromSnapshot(snapshot);

            if (autoRemoveCompleted && shouldAutoRemove(draft))
            {
                removedDuringRestore = true;
                continue;
            }

            drafts.Add(draft);
        }

        return new DraftLoadResult(drafts, removedDuringRestore);
    }

    public List<UploadDraftSnapshot> CreateSnapshots(IEnumerable<UploadDraft> drafts)
    {
        return drafts?.Select(d => d.ToSnapshot()).ToList()
            ?? new List<UploadDraftSnapshot>();
    }
}
