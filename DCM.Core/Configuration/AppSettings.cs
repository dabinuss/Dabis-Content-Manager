using DCM.Core.Models;

namespace DCM.Core.Configuration;

public sealed class AppSettings
{
    /// <summary>
    /// Zuletzt verwendeter Videoordner für den Dateidialog.
    /// </summary>
    public string? LastVideoFolder { get; set; }

    /// <summary>
    /// Optionaler Standardordner für Thumbnails.
    /// </summary>
    public string? DefaultThumbnailFolder { get; set; }

    /// <summary>
    /// Standardplattform für neue Upload-Projekte.
    /// </summary>
    public PlatformType DefaultPlatform { get; set; } = PlatformType.YouTube;

    /// <summary>
    /// Optionale Standard-Playlist-ID (z. B. YouTube Playlist).
    /// </summary>
    public string? DefaultPlaylistId { get; set; }
}
