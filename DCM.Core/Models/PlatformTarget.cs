namespace DCM.Core.Models;

public sealed class PlatformTarget
{
    public PlatformType Platform { get; set; } = PlatformType.YouTube;

    /// <summary>
    /// Optionale Account-/Konto-ID (z. B. für spätere Multi-Account-Unterstützung).
    /// </summary>
    public string? AccountId { get; set; }

    /// <summary>
    /// Optionale Channel-ID (z. B. YouTube-Channel).
    /// </summary>
    public string? ChannelId { get; set; }

    /// <summary>
    /// Optionale Playlist-ID, in die hochgeladen werden soll.
    /// </summary>
    public string? PlaylistId { get; set; }
}
