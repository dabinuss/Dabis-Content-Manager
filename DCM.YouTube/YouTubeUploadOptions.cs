// DCM.YouTube/YouTubeUploadOptions.cs

namespace DCM.YouTube;

public sealed class YouTubeUploadOptions
{
    public string? PlaylistId { get; set; }

    public bool IsDraft { get; set; }

    // Erweiterbar für z. B. „Made for kids“, CategoryId, etc.
}
