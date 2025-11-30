using System;

namespace DCM.Core.Models;

public sealed class Template
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Anzeigename des Templates, z. B. "Standard YouTube Beschreibung".
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Plattform, für die dieses Template gedacht ist.
    /// </summary>
    public PlatformType Platform { get; set; } = PlatformType.YouTube;

    /// <summary>
    /// Optionale Beschreibung / Notiz.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Der eigentliche Template-Text mit Platzhaltern wie {{TITLE}}, {{DATE}}, {{TAGS}}.
    /// </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Markiert ein Standardtemplate pro Plattform.
    /// </summary>
    public bool IsDefault { get; set; }
}
