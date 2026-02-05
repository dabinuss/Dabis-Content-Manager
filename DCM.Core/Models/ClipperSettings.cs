namespace DCM.Core.Models;

/// <summary>
/// Einstellungen für den Highlight Clipper.
/// </summary>
public sealed class ClipperSettings
{
    /// <summary>
    /// Minimale Clip-Dauer in Sekunden.
    /// </summary>
    public int MinClipDurationSeconds { get; set; } = 15;

    /// <summary>
    /// Maximale Clip-Dauer in Sekunden.
    /// </summary>
    public int MaxClipDurationSeconds { get; set; } = 90;

    /// <summary>
    /// Maximale Anzahl an Kandidaten pro Draft.
    /// </summary>
    public int MaxCandidatesPerDraft { get; set; } = 5;

    /// <summary>
    /// Standard Crop-Modus.
    /// </summary>
    public CropMode DefaultCropMode { get; set; } = CropMode.AutoDetect;

    /// <summary>
    /// Ob Gesichtserkennung aktiviert ist.
    /// </summary>
    public bool EnableFaceDetection { get; set; } = true;

    /// <summary>
    /// Anzahl der Frames für die Gesichtsanalyse pro Clip.
    /// Mehr Frames = genauer aber langsamer.
    /// </summary>
    public int FaceDetectionFrameCount { get; set; } = 10;

    /// <summary>
    /// Minimale Konfidenz für Gesichtserkennung (0-1).
    /// </summary>
    public float FaceDetectionMinConfidence { get; set; } = 0.5f;

    /// <summary>
    /// Ob Untertitel standardmäßig aktiviert sind.
    /// </summary>
    public bool EnableSubtitlesByDefault { get; set; } = true;

    /// <summary>
    /// Standard-Untertitel-Einstellungen.
    /// </summary>
    public ClipSubtitleSettings SubtitleSettings { get; set; } = new();

    /// <summary>
    /// Ausgabebreite für gerenderte Clips.
    /// </summary>
    public int OutputWidth { get; set; } = 1080;

    /// <summary>
    /// Ausgabehöhe für gerenderte Clips.
    /// </summary>
    public int OutputHeight { get; set; } = 1920;

    /// <summary>
    /// Video-Qualität (CRF-Wert, niedriger = besser).
    /// </summary>
    public int VideoQuality { get; set; } = 23;

    /// <summary>
    /// Audio-Bitrate in kbps.
    /// </summary>
    public int AudioBitrate { get; set; } = 192;

    /// <summary>
    /// Ob gerenderte Clips automatisch als neue Drafts hinzugefügt werden.
    /// </summary>
    public bool AutoCreateDraftFromClip { get; set; } = true;

    /// <summary>
    /// Ob der Ausgabeordner nach dem Rendering geöffnet wird.
    /// </summary>
    public bool OpenOutputFolderAfterRender { get; set; } = true;

    /// <summary>
    /// Ob der Kandidaten-Cache verwendet wird.
    /// </summary>
    public bool UseCandidateCache { get; set; } = true;

    /// <summary>
    /// LLM-Prompt für die Highlight-Bewertung.
    /// </summary>
    public string? CustomScoringPrompt { get; set; }

    /// <summary>
    /// Erstellt eine tiefe Kopie der Einstellungen.
    /// </summary>
    public ClipperSettings DeepCopy()
    {
        return new ClipperSettings
        {
            MinClipDurationSeconds = MinClipDurationSeconds,
            MaxClipDurationSeconds = MaxClipDurationSeconds,
            MaxCandidatesPerDraft = MaxCandidatesPerDraft,
            DefaultCropMode = DefaultCropMode,
            EnableFaceDetection = EnableFaceDetection,
            FaceDetectionFrameCount = FaceDetectionFrameCount,
            FaceDetectionMinConfidence = FaceDetectionMinConfidence,
            EnableSubtitlesByDefault = EnableSubtitlesByDefault,
            SubtitleSettings = SubtitleSettings.Clone(),
            OutputWidth = OutputWidth,
            OutputHeight = OutputHeight,
            VideoQuality = VideoQuality,
            AudioBitrate = AudioBitrate,
            AutoCreateDraftFromClip = AutoCreateDraftFromClip,
            OpenOutputFolderAfterRender = OpenOutputFolderAfterRender,
            UseCandidateCache = UseCandidateCache,
            CustomScoringPrompt = CustomScoringPrompt
        };
    }
}
