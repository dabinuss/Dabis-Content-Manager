using DCM.Core.Configuration;

namespace DCM.Core.Models;

/// <summary>
/// Observable-Version der ChannelPersona mit automatischer Change-Notification.
/// </summary>
public sealed class ObservableChannelPersona : ObservableObject
{
    private string? _name;
    private string? _channelName;
    private string? _language;
    private string? _toneOfVoice;
    private string? _contentType;
    private string? _targetAudience;
    private string? _additionalInstructions;

    public string? Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string? ChannelName
    {
        get => _channelName;
        set => SetProperty(ref _channelName, value);
    }

    /// <summary>
    /// Sprache z.B. "de-DE"
    /// </summary>
    public string? Language
    {
        get => _language;
        set => SetProperty(ref _language, value);
    }

    /// <summary>
    /// Tonalität z.B. "sarkastisch", "locker"
    /// </summary>
    public string? ToneOfVoice
    {
        get => _toneOfVoice;
        set => SetProperty(ref _toneOfVoice, value);
    }

    /// <summary>
    /// Content-Art z.B. "Gaming Highlights"
    /// </summary>
    public string? ContentType
    {
        get => _contentType;
        set => SetProperty(ref _contentType, value);
    }

    /// <summary>
    /// Zielgruppe z.B. "Casual Gamer, 18-34"
    /// </summary>
    public string? TargetAudience
    {
        get => _targetAudience;
        set => SetProperty(ref _targetAudience, value);
    }

    public string? AdditionalInstructions
    {
        get => _additionalInstructions;
        set => SetProperty(ref _additionalInstructions, value);
    }

    /// <summary>
    /// Erstellt eine Kopie als einfache ChannelPersona (für Serialisierung).
    /// </summary>
    public ChannelPersona ToChannelPersona()
    {
        return new ChannelPersona
        {
            Name = Name,
            ChannelName = ChannelName,
            Language = Language,
            ToneOfVoice = ToneOfVoice,
            ContentType = ContentType,
            TargetAudience = TargetAudience,
            AdditionalInstructions = AdditionalInstructions
        };
    }

    /// <summary>
    /// Lädt Werte aus einer einfachen ChannelPersona.
    /// </summary>
    public void LoadFrom(ChannelPersona? source)
    {
        var data = source ?? new ChannelPersona();
        Name = data.Name;
        ChannelName = data.ChannelName;
        Language = data.Language;
        ToneOfVoice = data.ToneOfVoice;
        ContentType = data.ContentType;
        TargetAudience = data.TargetAudience;
        AdditionalInstructions = data.AdditionalInstructions;
    }
}
