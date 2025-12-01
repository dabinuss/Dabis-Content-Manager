using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DCM.Core.Models;

namespace DCM.Core.Services;

/// <summary>
/// Orchestriert den Upload-Flow:
/// - optional Template anwenden
/// - Projekt validieren
/// - passenden IPlatformClient auswählen und Upload ausführen
/// </summary>
public sealed class UploadService
{
    private readonly IReadOnlyList<IPlatformClient> _platformClients;
    private readonly TemplateService _templateService;

    public UploadService(IEnumerable<IPlatformClient> platformClients, TemplateService templateService)
    {
        if (platformClients is null) throw new ArgumentNullException(nameof(platformClients));

        _platformClients = platformClients.ToList();
        _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
    }

    public async Task<UploadResult> UploadAsync(
        UploadProject project,
        Template? template,
        IProgress<UploadProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));

        // Template anwenden (falls angegeben)
        if (template is not null && !string.IsNullOrWhiteSpace(template.Body))
        {
            // Nur überschreiben, wenn die Beschreibung noch leer ist,
            // damit der User seine manuelle Eingabe nicht verliert.
            if (string.IsNullOrWhiteSpace(project.Description))
            {
                var text = _templateService.ApplyTemplate(template.Body, project);
                project.Description = text;
            }
        }

        // Validierung des Projekts
        project.Validate();

        // passenden Plattform-Client finden
        var client = _platformClients.FirstOrDefault(c => c.Platform == project.Platform);
        if (client is null)
        {
            return UploadResult.Failed($"Kein Upload-Client für Plattform '{project.Platform}' registriert.");
        }

        // Upload über den jeweiligen Client
        return await client.UploadAsync(project, progress, cancellationToken).ConfigureAwait(false);
    }
}