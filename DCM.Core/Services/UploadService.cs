using System.Text;
using DCM.Core.Logging;
using DCM.Core.Models;

namespace DCM.Core.Services;

/// <summary>
/// Orchestriert den Upload-Flow:
/// - optional Preset-Template anwenden
/// - Projekt validieren
/// - passenden IPlatformClient auswählen und Upload ausführen
/// </summary>
public sealed class UploadService
{
    private const int YouTubeDescriptionLimit = 5000;
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
        UploadPreset? preset,
        IProgress<UploadProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));

        _logger.Info($"Upload gestartet für: {project.Title}", "UploadService");
        _logger.Debug($"Plattform: {project.Platform}, Video: {project.VideoFilePath}", "UploadService");

        // Preset-Template anwenden (falls angegeben)
        if (preset is not null && !string.IsNullOrWhiteSpace(preset.DescriptionTemplate))
        {
            if (string.IsNullOrWhiteSpace(project.Description))
            {
                _logger.Debug($"Preset-Template '{preset.Name}' wird angewendet", "UploadService");
                var text = _templateService.ApplyTemplate(preset.DescriptionTemplate, project);
                project.Description = text;
            }
            else
            {
                _logger.Debug("Preset-Template uebersprungen - Beschreibung bereits vorhanden", "UploadService");
            }
        }

        var sanitizedDescription = SanitizeDescription(project.Description);
        if (!string.Equals(project.Description, sanitizedDescription, StringComparison.Ordinal))
        {
            var originalLength = project.Description?.Length ?? 0;
            var sanitizedLength = sanitizedDescription.Length;

            if (sanitizedLength < originalLength)
            {
                _logger.Warning(
                    $"Beschreibung wurde auf {sanitizedLength} Zeichen gekürzt (maximal erlaubt: {YouTubeDescriptionLimit}).",
                    "UploadService");
            }
            else
            {
                _logger.Warning("Beschreibung enthielt ungültige Zeichen und wurde bereinigt.", "UploadService");
            }

            project.Description = sanitizedDescription;
        }
        else
        {
            project.Description = sanitizedDescription;
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

    private static string SanitizeDescription(string? rawText)
    {
        if (string.IsNullOrEmpty(rawText))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(rawText.Length);

        foreach (var ch in rawText)
        {
            if (char.IsControl(ch) && ch != '\r' && ch != '\n' && ch != '\t')
            {
                continue;
            }

            builder.Append(ch);
        }

        var sanitized = builder.ToString();
        if (sanitized.Length <= YouTubeDescriptionLimit)
        {
            return sanitized;
        }

        return sanitized[..YouTubeDescriptionLimit];
    }
}
