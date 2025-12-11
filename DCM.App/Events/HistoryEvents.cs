using System;
using DCM.Core.Models;

namespace DCM.App.Events;

public readonly record struct HistoryFilterChangedEvent;

public readonly record struct HistoryClearRequestedEvent;

public readonly record struct HistoryEntryOpenRequestedEvent(UploadHistoryEntry? Entry);

public readonly record struct HistoryLinkOpenRequestedEvent(Uri Uri);
