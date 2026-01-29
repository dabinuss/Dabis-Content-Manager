namespace DCM.Core.Configuration;

/// <summary>
/// Observable-Version der TranscriptionSettings mit automatischer Change-Notification.
/// </summary>
public sealed class ObservableTranscriptionSettings : ObservableObject
{
    private bool _autoTranscribeOnVideoSelect;
    private WhisperModelSize _modelSize = WhisperModelSize.Small;
    private string? _language = "de";

    /// <summary>
    /// Gibt an, ob bei Video-Auswahl automatisch transkribiert werden soll.
    /// </summary>
    public bool AutoTranscribeOnVideoSelect
    {
        get => _autoTranscribeOnVideoSelect;
        set => SetProperty(ref _autoTranscribeOnVideoSelect, value);
    }

    /// <summary>
    /// Gewünschte Whisper-Modellgröße.
    /// </summary>
    public WhisperModelSize ModelSize
    {
        get => _modelSize;
        set => SetProperty(ref _modelSize, value);
    }

    /// <summary>
    /// Sprache für die Transkription (z.B. "de" für Deutsch).
    /// Null bedeutet automatische Erkennung.
    /// </summary>
    public string? Language
    {
        get => _language;
        set => SetProperty(ref _language, value);
    }

    /// <summary>
    /// Gibt an, ob die Transkription konfiguriert und einsatzbereit ist.
    /// </summary>
    public bool IsConfigured => true;

    /// <summary>
    /// Erstellt eine Kopie als einfache TranscriptionSettings (für Serialisierung).
    /// </summary>
    public TranscriptionSettings ToTranscriptionSettings()
    {
        return new TranscriptionSettings
        {
            AutoTranscribeOnVideoSelect = AutoTranscribeOnVideoSelect,
            ModelSize = ModelSize,
            Language = Language
        };
    }

    /// <summary>
    /// Lädt Werte aus einfachen TranscriptionSettings.
    /// </summary>
    public void LoadFrom(TranscriptionSettings? source)
    {
        var data = source ?? new TranscriptionSettings();
        AutoTranscribeOnVideoSelect = data.AutoTranscribeOnVideoSelect;
        ModelSize = data.ModelSize;
        Language = data.Language;
    }
}
