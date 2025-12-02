namespace DCM.Core;

/// <summary>
/// Zentrale Konstanten für die Anwendung.
/// </summary>
public static class Constants
{
    /// <summary>
    /// Name der Anwendung, verwendet für Ordnernamen, Fenstertitel, etc.
    /// </summary>
    public const string ApplicationName = "DabisContentManager";

    /// <summary>
    /// Ordnername im AppData-Verzeichnis.
    /// </summary>
    public const string AppDataFolderName = "DabisContentManager";

    /// <summary>
    /// Dateiname für die Einstellungen.
    /// </summary>
    public const string SettingsFileName = "settings.json";

    /// <summary>
    /// Dateiname für die Templates.
    /// </summary>
    public const string TemplatesFileName = "templates.json";

    /// <summary>
    /// Dateiname für die Upload-Historie.
    /// </summary>
    public const string UploadHistoryFileName = "upload_history.json";

    /// <summary>
    /// Ordnername für YouTube-Tokens.
    /// </summary>
    public const string YouTubeTokensFolderName = "youtube_tokens";

    /// <summary>
    /// Dateiname für YouTube Client Secrets.
    /// </summary>
    public const string YouTubeClientSecretsFileName = "youtube_client_secrets.json";

    /// <summary>
    /// Maximale Thumbnail-Größe in Bytes (2 MB für YouTube).
    /// </summary>
    public const long MaxThumbnailSizeBytes = 2 * 1024 * 1024;

    /// <summary>
    /// Delay nach Video-Upload bevor Thumbnail gesetzt wird (YouTube benötigt Zeit).
    /// </summary>
    public const int YouTubeThumbnailDelayMs = 1500;

    /// <summary>
    /// Standard-Scheduling-Zeit.
    /// </summary>
    public const string DefaultSchedulingTime = "18:00";

    /// <summary>
    /// Gibt den Pfad zum AppData-Ordner der Anwendung zurück.
    /// Erstellt den Ordner falls er nicht existiert.
    /// </summary>
    public static string GetAppDataFolder()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(appData, AppDataFolderName);

        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        return folder;
    }
}