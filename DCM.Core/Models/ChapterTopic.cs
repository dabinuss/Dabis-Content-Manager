using System.Collections.Generic;

namespace DCM.Core.Models;

public sealed record ChapterTopic(string Title, IReadOnlyList<string> Keywords, string? AnchorText);
