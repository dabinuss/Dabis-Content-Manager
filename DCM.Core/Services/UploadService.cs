using DCM.Core.Logging;
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
    private readonly IAppLogger _logger;

    public UploadService(
        IEnumerable<IPlatformClient> platformClients,
        TemplateService templateService,
        IAppLogger? logger = null)
    {
        if (platformClients is null) throw new ArgumentNullException(nameof(platformClients));

        _platformClients = platformClients.ToList();
        _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
        _logger = logger ?? AppLogger.Instance;
    }

    public async Task<UploadResult> UploadAsync(
        UploadProject project,
        Template? template,
        IProgress<UploadProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));

        _logger.Info($"Upload gestartet für: {project.Title}", "UploadService");
        _logger.Debug($"Plattform: {project.Platform}, Video: {project.VideoFilePath}", "UploadService");

        // Template anwenden (falls angegeben)
        if (template is not null && !string.IsNullOrWhiteSpace(template.Body))
        {
            if (string.IsNullOrWhiteSpace(project.Description))
            {
                _logger.Debug($"Template '{template.Name}' wird angewendet", "UploadService");
                var text = _templateService.ApplyTemplate(template.Body, project);
                project.Description = text;
            }
            else
            {
                _logger.Debug("Template übersprungen - Beschreibung bereits vorhanden", "UploadService");
            }
        }

        // Validierung des Projekts
        try
        {
            project.Validate();
            _logger.Debug("Projekt-Validierung erfolgreich", "UploadService");
        }
        catch (Exception ex)
        {
            _logger.Error($"Projekt-Validierung fehlgeschlagen: {ex.Message}", "UploadService", ex);
            return UploadResult.Failed($"Validierungsfehler: {ex.Message}");
        }

        // passenden Plattform-Client finden
        var client = _platformClients.FirstOrDefault(c => c.Platform == project.Platform);
        if (client is null)
        {
            var errorMsg = $"Kein Upload-Client für Plattform '{project.Platform}' registriert.";
            _logger.Error(errorMsg, "UploadService");
            return UploadResult.Failed(errorMsg);
        }

        _logger.Debug($"Plattform-Client gefunden: {client.GetType().Name}", "UploadService");

        // Upload über den jeweiligen Client
        try
        {
            var result = await client.UploadAsync(project, progress, cancellationToken).ConfigureAwait(false);

            if (result.Success)
            {
                _logger.Info($"Upload erfolgreich: {result.VideoUrl}", "UploadService");
            }
            else
            {
                _logger.Warning($"Upload fehlgeschlagen: {result.ErrorMessage}", "UploadService");
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.Warning("Upload wurde abgebrochen", "UploadService");
            return UploadResult.Failed("Upload wurde abgebrochen.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Unerwarteter Fehler beim Upload: {ex.Message}", "UploadService", ex);
            return UploadResult.Failed($"Unerwarteter Fehler: {ex.Message}");
        }
    }
}