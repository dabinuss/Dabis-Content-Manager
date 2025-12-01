using System;

namespace DCM.Core.Models;

public sealed class ChannelPersona
{
    public string? Name { get; set; }
    public string? ChannelName { get; set; }
    public string? Language { get; set; }          // z. B. "de-DE"
    public string? ToneOfVoice { get; set; }       // z. B. "sarkastisch", "locker"
    public string? ContentType { get; set; }       // z. B. "Gaming Highlights"
    public string? TargetAudience { get; set; }    // z. B. "Casual Gamer, 18-34"
    public string? AdditionalInstructions { get; set; }
}
