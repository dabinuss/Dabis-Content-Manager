namespace DCM.Core.Configuration;

/// <summary>
/// Definiert die verfügbaren Whisper-Modellgrößen.
/// </summary>
public enum WhisperModelSize
{
    /// <summary>
    /// Tiny-Modell (~75 MB). Schnellste Option, geringste Genauigkeit.
    /// </summary>
    Tiny,

    /// <summary>
    /// Base-Modell (~142 MB). Schnell mit akzeptabler Genauigkeit.
    /// </summary>
    Base,

    /// <summary>
    /// Small-Modell (~466 MB). Guter Kompromiss zwischen Geschwindigkeit und Genauigkeit.
    /// </summary>
    Small,

    /// <summary>
    /// Medium-Modell (~1.5 GB). Hohe Genauigkeit, langsamer.
    /// </summary>
    Medium,

    /// <summary>
    /// Large-Modell (~2.9 GB). Höchste Genauigkeit, am langsamsten.
    /// </summary>
    Large
}

/// <summary>
/// Hilfsmethoden für WhisperModelSize.
/// </summary>
public static class WhisperModelSizeExtensions
{
    /// <summary>
    /// Gibt den Dateinamen des Modells zurück.
    /// </summary>
    public static string GetFileName(this WhisperModelSize size)
    {
        return size switch
        {
            WhisperModelSize.Tiny => "ggml-tiny.bin",
            WhisperModelSize.Base => "ggml-base.bin",
            WhisperModelSize.Small => "ggml-small.bin",
            WhisperModelSize.Medium => "ggml-medium.bin",
            WhisperModelSize.Large => "ggml-large-v3.bin",
            _ => "ggml-small.bin"
        };
    }

    /// <summary>
    /// Gibt die Download-URL für das Modell zurück (Hugging Face).
    /// </summary>
    public static string GetDownloadUrl(this WhisperModelSize size)
    {
        var fileName = size.GetFileName();
        return $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{fileName}";
    }

    /// <summary>
    /// Gibt die ungefähre Dateigröße in Bytes zurück.
    /// </summary>
    public static long GetApproximateSizeBytes(this WhisperModelSize size)
    {
        return size switch
        {
            WhisperModelSize.Tiny => 75L * 1024 * 1024,
            WhisperModelSize.Base => 142L * 1024 * 1024,
            WhisperModelSize.Small => 466L * 1024 * 1024,
            WhisperModelSize.Medium => 1536L * 1024 * 1024,
            WhisperModelSize.Large => 2952L * 1024 * 1024,
            _ => 466L * 1024 * 1024
        };
    }

    /// <summary>
    /// Gibt eine benutzerfreundliche Größenbeschreibung zurück.
    /// </summary>
    public static string GetSizeDescription(this WhisperModelSize size)
    {
        return size switch
        {
            WhisperModelSize.Tiny => "~75 MB",
            WhisperModelSize.Base => "~142 MB",
            WhisperModelSize.Small => "~466 MB",
            WhisperModelSize.Medium => "~1.5 GB",
            WhisperModelSize.Large => "~2.9 GB",
            _ => "Unbekannt"
        };
    }
}