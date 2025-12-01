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
    /// Ob beim Start automatisch versucht werden soll, mit YouTube zu verbinden (falls Tokens vorhanden sind).
    /// </summary>
    public bool AutoConnectYouTube { get; set; } = true;

    /// <summary>
    /// Standard-Sichtbarkeit für neue Uploads.
    /// </summary>
    public VideoVisibility DefaultVisibility { get; set; } = VideoVisibility.Unlisted;

    /// <summary>
    /// Ob das ausgewählte (Standard-)Template automatisch angewendet werden soll, wenn möglich.
    /// </summary>
    public bool AutoApplyDefaultTemplate { get; set; } = true;

    /// <summary>
    /// Ob nach erfolgreichem Upload der Browser mit der Video-URL geöffnet werden soll.
    /// </summary>
    public bool OpenBrowserAfterUpload { get; set; } = false;

    /// <summary>
    /// Kanal-Persona für Content-Suggestions.
    /// </summary>
    public ChannelPersona Persona { get; set; } = new();

    /// <summary>
    /// LLM-Einstellungen für KI-gestützte Content-Generierung.
    /// </summary>
    public LlmSettings Llm { get; set; } = new();
}