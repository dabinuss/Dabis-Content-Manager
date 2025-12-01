using DCM.Core.Models;

namespace DCM.Core.Configuration;

public sealed class AppSettings
{
    /// <summary>
    /// Zuletzt verwendeter Videoordner für den Dateidialog.
    /// </summary>
    public string? LastVideoFolder { get; set; }

    /// <summary>
    /// Optionaler Standardordner für Videodateien (für Dateidialog).
    /// </summary>
    public string? DefaultVideoFolder { get; set; }

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

    /// <summary>
    /// Standard-Zeit für Scheduling (z. B. "18:00").
    /// </summary>
    public string? DefaultSchedulingTime { get; set; }

    /// <summary>
    /// Ob vor dem Start eines Uploads eine Bestätigung angezeigt werden soll.
    /// </summary>
    public bool ConfirmBeforeUpload { get; set; } = false;

    /// <summary>
    /// Pfad zu einem externen Bildbearbeitungsprogramm (für Thumbnails etc.).
    /// </summary>
    public string? ExternalImageEditorPath { get; set; }
}
