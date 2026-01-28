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

    public const string PresetsFileName = "presets.json";

    /// <summary>
    /// Dateiname für die Upload-Historie.
    /// </summary>
    public const string UploadHistoryFileName = "upload_history.json";

    /// <summary>
    /// Dateiname für die Log-Datei.
    /// </summary>
    public const string LogFileName = "app.log";

    /// <summary>
    /// Maximale Größe einer einzelnen Log-Datei (Rotation).
    /// </summary>
    public static readonly long LogFileMaxBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Anzahl an Log-Dateien, die rotiert aufbewahrt werden.
    /// </summary>
    public static readonly int LogFileMaxCount = 5;

    /// <summary>
    /// Ordnername für YouTube-Tokens.
    /// </summary>
    public const string YouTubeTokensFolderName = "youtube_tokens";

    /// <summary>
    /// Dateiname für YouTube Client Secrets.
    /// </summary>
    public const string YouTubeClientSecretsFileName = "youtube_client_secrets.json";

    /// <summary>
    /// Ordnername für Whisper-Modelle.
    /// </summary>
    public const string WhisperModelsFolderName = "whisper_models";

    /// <summary>
    /// Ordnername für FFmpeg.
    /// </summary>
    public const string FFmpegFolderName = "ffmpeg";

    /// <summary>
    /// Ordnername fr gespeicherte Thumbnails.
    /// </summary>
    public const string ThumbnailsFolderName = "thumbnails";

    /// <summary>
    /// Ordnername fr Video-Vorschauen.
    /// </summary>
    public const string VideoPreviewFolderName = "video_previews";

    /// <summary>
    /// Maximale Cache-Größe für Thumbnails.
    /// </summary>
    public const long ThumbnailCacheMaxBytes = 1024L * 1024 * 1024;

    /// <summary>
    /// Maximale Cache-Größe für Video-Previews.
    /// </summary>
    public const long VideoPreviewCacheMaxBytes = 512L * 1024 * 1024;

    /// <summary>
    /// Aufbewahrungstage für verwaiste Thumbnails.
    /// </summary>
    public const int ThumbnailRetentionDays = 90;

    /// <summary>
    /// Aufbewahrungstage für verwaiste Video-Previews.
    /// </summary>
    public const int VideoPreviewRetentionDays = 30;

    /// <summary>
    /// Ordnername für ausgelagerte Transkripte der Upload-Drafts.
    /// </summary>
    public const string DraftTranscriptsFolderName = "draft_transcripts";

    /// <summary>
    /// Ordnername für temporäre Transkriptionsdateien (im System-Temp).
    /// </summary>
    public const string TranscriptionTempFolderName = "DCM_Transcription";

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

    private static string? _appDataFolder;

    /// <summary>
    /// Pfad zum AppData-Ordner der Anwendung.
    /// Erstellt den Ordner falls er nicht existiert.
    /// </summary>
    public static string AppDataFolder
    {
        get
        {
            if (_appDataFolder is not null)
            {
                return _appDataFolder;
            }

#if DCM_PORTABLE
            var baseDir = AppContext.BaseDirectory ?? string.Empty;
            _appDataFolder = Path.Combine(baseDir, AppDataFolderName);
#else
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _appDataFolder = Path.Combine(appData, AppDataFolderName);
#endif

            if (!Directory.Exists(_appDataFolder))
            {
                Directory.CreateDirectory(_appDataFolder);
            }

            return _appDataFolder;
        }
    }

    /// <summary>
    /// Pfad zum Whisper-Modelle-Ordner.
    /// Erstellt den Ordner falls er nicht existiert.
    /// </summary>
    public static string WhisperModelsFolder
    {
        get
        {
            var folder = Path.Combine(AppDataFolder, WhisperModelsFolderName);

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            return folder;
        }
    }

    /// <summary>
    /// Pfad zum FFmpeg-Ordner.
    /// Erstellt den Ordner falls er nicht existiert.
    /// </summary>
    public static string FFmpegFolder
    {
        get
        {
            var folder = Path.Combine(AppDataFolder, FFmpegFolderName);

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            return folder;
        }
    }

    /// <summary>
    /// Pfad zum Thumbnail-Ordner.
    /// Erstellt den Ordner falls er nicht existiert.
    /// </summary>
    public static string ThumbnailsFolder
    {
        get
        {
            var folder = Path.Combine(AppDataFolder, ThumbnailsFolderName);

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            return folder;
        }
    }

    /// <summary>
    /// Pfad zum Video-Vorschau-Ordner.
    /// Erstellt den Ordner falls er nicht existiert.
    /// </summary>
    public static string VideoPreviewFolder
    {
        get
        {
            var folder = Path.Combine(AppDataFolder, VideoPreviewFolderName);

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            return folder;
        }
    }

    /// <summary>
    /// Pfad zum Draft-Transkript-Ordner.
    /// Erstellt den Ordner falls er nicht existiert.
    /// </summary>
    public static string DraftTranscriptsFolder
    {
        get
        {
            var folder = Path.Combine(AppDataFolder, DraftTranscriptsFolderName);

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            return folder;
        }
    }

    /// <summary>
    /// Pfad zum temporären Transkriptions-Ordner (im System-Temp).
    /// Erstellt den Ordner falls er nicht existiert.
    /// </summary>
    public static string TranscriptionTempFolder
    {
        get
        {
            var folder = Path.Combine(Path.GetTempPath(), TranscriptionTempFolderName);

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            return folder;
        }
    }

    /// <summary>
    /// Gibt den Pfad zum AppData-Ordner der Anwendung zurück.
    /// Erstellt den Ordner falls er nicht existiert.
    /// </summary>
    [Obsolete("Verwende Constants.AppDataFolder stattdessen.")]
    public static string GetAppDataFolder() => AppDataFolder;
}
