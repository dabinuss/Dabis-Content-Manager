namespace DCM.Core.Configuration;

public sealed class LlmSettings
{
    public string Mode { get; set; } = "None";
    
    public string? LocalModelPath { get; set; }
    
    public int MaxTokens { get; set; } = 256;
    
    public float Temperature { get; set; } = 0.3f;
}