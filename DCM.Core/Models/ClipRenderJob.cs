namespace DCM.Core.Models;

/// <summary>
/// Repräsentiert einen Render-Auftrag für einen Clip.
/// Enthält alle Parameter für FFmpeg-Verarbeitung.
/// </summary>
public sealed class ClipRenderJob
{
    /// <summary>
    /// Eindeutige ID des Render-Jobs.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// ID des zugehörigen ClipCandidate.
    /// </summary>
    public Guid CandidateId { get; set; }

    /// <summary>
    /// Vollständiger ClipCandidate (optional, z.B. für Title-Generierung).
    /// </summary>
    public ClipCandidate? Candidate { get; set; }

    /// <summary>
    /// ID des Quell-Drafts.
    /// </summary>
    public Guid SourceDraftId { get; set; }

    /// <summary>
    /// Pfad zum Quell-Video.
    /// </summary>
    public required string SourceVideoPath { get; set; }

    /// <summary>
    /// Pfad für das gerenderte Ausgabe-Video.
    /// </summary>
    public required string OutputPath { get; set; }

    /// <summary>
    /// Startzeit im Quell-Video.
    /// </summary>
    public TimeSpan StartTime { get; set; }

    /// <summary>
    /// Endzeit im Quell-Video.
    /// </summary>
    public TimeSpan EndTime { get; set; }

    /// <summary>
    /// Dauer des Clips.
    /// </summary>
    public TimeSpan Duration => EndTime - StartTime;

    /// <summary>
    /// Crop-Modus für das Portrait-Format.
    /// </summary>
    public CropMode CropMode { get; set; } = CropMode.AutoDetect;

    /// <summary>
    /// Manuelle X-Offset für Center-Crop (nur bei CropMode.Manual).
    /// Wert von -1.0 (links) bis 1.0 (rechts), 0 = Mitte.
    /// </summary>
    public double ManualCropOffsetX { get; set; } = 0.0;

    /// <summary>
    /// Konfiguration für Split-Layout (nur bei CropMode.SplitLayout).
    /// </summary>
    public SplitLayoutConfig? SplitLayout { get; set; }

    /// <summary>
    /// Zielbreite des Ausgabe-Videos in Pixeln.
    /// Standard: 1080 für TikTok/Reels.
    /// </summary>
    public int OutputWidth { get; set; } = 1080;

    /// <summary>
    /// Zielhöhe des Ausgabe-Videos in Pixeln.
    /// Standard: 1920 für 9:16 Portrait.
    /// </summary>
    public int OutputHeight { get; set; } = 1920;

    /// <summary>
    /// Ob Untertitel eingebrannt werden sollen.
    /// </summary>
    public bool BurnSubtitles { get; set; } = true;

    /// <summary>
    /// Pfad zur ASS-Untertiteldatei (wenn BurnSubtitles = true).
    /// </summary>
    public string? SubtitlePath { get; set; }

    /// <summary>
    /// Untertitel-Einstellungen für die Generierung.
    /// </summary>
    public ClipSubtitleSettings? SubtitleSettings { get; set; }

    /// <summary>
    /// Transkript-Segmente für Untertitel-Generierung.
    /// </summary>
    public IReadOnlyList<ClipSubtitleSegment>? SubtitleSegments { get; set; }

    /// <summary>
    /// Zeitpunkt der Job-Erstellung.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optionale Metadaten für den generierten Draft.
    /// </summary>
    public string? GeneratedTitle { get; set; }

    /// <summary>
    /// Video-Qualität (CRF-Wert für FFmpeg, niedriger = besser).
    /// Standard: 23 für gute Qualität bei akzeptabler Dateigröße.
    /// </summary>
    public int VideoQuality { get; set; } = 23;

    /// <summary>
    /// Video-Codec für die Ausgabe.
    /// </summary>
    public string VideoCodec { get; set; } = "libx264";

    /// <summary>
    /// Audio-Codec für die Ausgabe.
    /// </summary>
    public string AudioCodec { get; set; } = "aac";

    /// <summary>
    /// Audio-Bitrate in kbps.
    /// </summary>
    public int AudioBitrate { get; set; } = 192;

    /// <summary>
    /// Optionaler Pfad zu einem Logo-Bild für Overlay.
    /// </summary>
    public string? LogoPath { get; set; }

    /// <summary>
    /// Logo-Größe in Pixeln (Breite). Höhe wird proportional skaliert.
    /// Fallback-Wert, falls keine relative Skalierung gesetzt ist.
    /// </summary>
    public int LogoSize { get; set; } = 80;

    /// <summary>
    /// Abstand des Logos vom Rand in Pixeln.
    /// Fallback-Wert für ältere Renderpfade.
    /// </summary>
    public int LogoMargin { get; set; } = 30;

    /// <summary>
    /// Relative Logo-Größe zur Ausgabebreite (0.05 - 0.5).
    /// </summary>
    public double LogoScale { get; set; } = 0.15;

    /// <summary>
    /// Relative X-Position (0-1), bezogen auf das Zentrum des Logos.
    /// </summary>
    public double LogoPositionX { get; set; } = 0.9;

    /// <summary>
    /// Relative Y-Position (0-1), bezogen auf das Zentrum des Logos.
    /// </summary>
    public double LogoPositionY { get; set; } = 0.05;
}

/// <summary>
/// Ein einzelnes Untertitel-Segment für die ASS-Generierung.
/// </summary>
public sealed class ClipSubtitleSegment
{
    /// <summary>
    /// Startzeit relativ zum Clip-Beginn.
    /// </summary>
    public required TimeSpan Start { get; init; }

    /// <summary>
    /// Endzeit relativ zum Clip-Beginn.
    /// </summary>
    public required TimeSpan End { get; init; }

    /// <summary>
    /// Der anzuzeigende Text.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Einzelne Wörter mit Timing für Word-by-Word Highlighting.
    /// </summary>
    public IReadOnlyList<ClipSubtitleWord>? Words { get; init; }
}

/// <summary>
/// Ein einzelnes Wort für Word-by-Word Highlighting.
/// </summary>
public sealed class ClipSubtitleWord
{
    /// <summary>
    /// Startzeit relativ zum Clip-Beginn.
    /// </summary>
    public required TimeSpan Start { get; init; }

    /// <summary>
    /// Endzeit relativ zum Clip-Beginn.
    /// </summary>
    public required TimeSpan End { get; init; }

    /// <summary>
    /// Das Wort.
    /// </summary>
    public required string Text { get; init; }
}
