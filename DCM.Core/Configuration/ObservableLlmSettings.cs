namespace DCM.Core.Configuration;

/// <summary>
/// Observable-Version der LlmSettings mit automatischer Change-Notification.
/// </summary>
public sealed class ObservableLlmSettings : ObservableObject
{
    private LlmMode _mode = LlmMode.Off;
    private LlmModelPreset _modelPreset = LlmModelPreset.Phi3Mini4kQ4;
    private string? _localModelPath;
    private LlmModelType _modelType = LlmModelType.Auto;
    private string? _systemPrompt;
    private int _maxTokens = 256;
    private float _temperature = 0.3f;
    private int _contextSize = 4096;
    private string? _titleCustomPrompt;
    private string? _descriptionCustomPrompt;
    private string? _tagsCustomPrompt;

    /// <summary>
    /// LLM-Modus: Off oder Local.
    /// </summary>
    public LlmMode Mode
    {
        get => _mode;
        set => SetProperty(ref _mode, value);
    }

    /// <summary>
    /// Ausgewähltes Modell-Preset für automatischen Download.
    /// </summary>
    public LlmModelPreset ModelPreset
    {
        get => _modelPreset;
        set => SetProperty(ref _modelPreset, value);
    }

    /// <summary>
    /// Pfad zur lokalen GGUF-Modelldatei (für ModelPreset == Custom).
    /// </summary>
    public string? LocalModelPath
    {
        get => _localModelPath;
        set => SetProperty(ref _localModelPath, value);
    }

    /// <summary>
    /// Modelltyp für Prompt-Formatierung. Auto erkennt anhand des Dateinamens.
    /// </summary>
    public LlmModelType ModelType
    {
        get => _modelType;
        set => SetProperty(ref _modelType, value);
    }

    /// <summary>
    /// System-Prompt, der dem Modell als Kontext übergeben wird.
    /// </summary>
    public string? SystemPrompt
    {
        get => _systemPrompt;
        set => SetProperty(ref _systemPrompt, value);
    }

    /// <summary>
    /// Maximale Anzahl an Tokens für die Generierung.
    /// </summary>
    public int MaxTokens
    {
        get => _maxTokens;
        set => SetProperty(ref _maxTokens, value);
    }

    /// <summary>
    /// Temperatur für die Generierung (0.0 = deterministisch, 1.0 = kreativ).
    /// </summary>
    public float Temperature
    {
        get => _temperature;
        set => SetProperty(ref _temperature, value);
    }

    /// <summary>
    /// Kontext-Größe in Tokens.
    /// </summary>
    public int ContextSize
    {
        get => _contextSize;
        set => SetProperty(ref _contextSize, value);
    }

    /// <summary>
    /// Optionale Custom-Anweisung für Titel-Generierung.
    /// </summary>
    public string? TitleCustomPrompt
    {
        get => _titleCustomPrompt;
        set => SetProperty(ref _titleCustomPrompt, value);
    }

    /// <summary>
    /// Optionale Custom-Anweisung für Beschreibungs-Generierung.
    /// </summary>
    public string? DescriptionCustomPrompt
    {
        get => _descriptionCustomPrompt;
        set => SetProperty(ref _descriptionCustomPrompt, value);
    }

    /// <summary>
    /// Optionale Custom-Anweisung für Tag-Generierung.
    /// </summary>
    public string? TagsCustomPrompt
    {
        get => _tagsCustomPrompt;
        set => SetProperty(ref _tagsCustomPrompt, value);
    }

    /// <summary>
    /// Gibt an, ob der Local-Modus aktiv ist.
    /// </summary>
    public bool IsLocalMode => Mode == LlmMode.Local;

    /// <summary>
    /// Gibt an, ob LLM-Funktionen deaktiviert sind.
    /// </summary>
    public bool IsOff => Mode == LlmMode.Off;

    /// <summary>
    /// Gibt den effektiven Modellpfad zurück (Custom-Pfad oder Preset-Pfad).
    /// </summary>
    public string? GetEffectiveModelPath()
    {
        if (ModelPreset == LlmModelPreset.Custom)
        {
            return LocalModelPath;
        }

        var fileName = ModelPreset.GetFileName();
        if (fileName is null)
        {
            return LocalModelPath;
        }

        return System.IO.Path.Combine(Constants.LlmModelsFolder, fileName);
    }

    /// <summary>
    /// Gibt den effektiven Modelltyp zurück (Preset-Typ oder konfigurierten Typ).
    /// </summary>
    public LlmModelType GetEffectiveModelType()
    {
        if (ModelPreset != LlmModelPreset.Custom)
        {
            return ModelPreset.GetModelType();
        }

        return ModelType;
    }

    /// <summary>
    /// Erstellt eine Kopie als einfache LlmSettings (für Serialisierung).
    /// </summary>
    public LlmSettings ToLlmSettings()
    {
        return new LlmSettings
        {
            Mode = Mode,
            ModelPreset = ModelPreset,
            LocalModelPath = LocalModelPath,
            ModelType = ModelType,
            SystemPrompt = SystemPrompt,
            MaxTokens = MaxTokens,
            Temperature = Temperature,
            ContextSize = ContextSize,
            TitleCustomPrompt = TitleCustomPrompt,
            DescriptionCustomPrompt = DescriptionCustomPrompt,
            TagsCustomPrompt = TagsCustomPrompt
        };
    }

    /// <summary>
    /// Lädt Werte aus einfachen LlmSettings.
    /// </summary>
    public void LoadFrom(LlmSettings? source)
    {
        var data = source ?? new LlmSettings();
        Mode = data.Mode;
        ModelPreset = data.ModelPreset;
        LocalModelPath = data.LocalModelPath;
        ModelType = data.ModelType;
        SystemPrompt = data.SystemPrompt;
        MaxTokens = data.MaxTokens;
        Temperature = data.Temperature;
        ContextSize = data.ContextSize;
        TitleCustomPrompt = data.TitleCustomPrompt;
        DescriptionCustomPrompt = data.DescriptionCustomPrompt;
        TagsCustomPrompt = data.TagsCustomPrompt;
    }
}
