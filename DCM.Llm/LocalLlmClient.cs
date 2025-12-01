using System.Text;
using DCM.Core.Configuration;
using DCM.Core.Services;
using LLama;
using LLama.Common;

namespace DCM.Llm;

/// <summary>
/// Lokaler LLM-Client basierend auf LLamaSharp.
/// Lädt ein GGUF-Modell und führt Inferenz lokal durch.
/// </summary>
public sealed class LocalLlmClient : ILlmClient, IDisposable
{
    private readonly LlmSettings _settings;
    private readonly object _lock = new();

    private LLamaWeights? _model;
    private LLamaContext? _context;
    private InteractiveExecutor? _executor;
    private bool _initialized;
    private bool _disposed;
    private string? _initError;

    public LocalLlmClient(LlmSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public bool IsReady
    {
        get
        {
            EnsureInitialized();
            return _initialized && _executor is not null;
        }
    }

    public string? InitializationError => _initError;

    public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return string.Empty;
        }

        EnsureInitialized();

        if (!_initialized || _executor is null)
        {
            return $"[LLM nicht verfügbar: {_initError ?? "Unbekannter Fehler"}]";
        }

        try
        {
            var inferenceParams = new InferenceParams
            {
                MaxTokens = _settings.MaxTokens > 0 ? _settings.MaxTokens : 256,
                Temperature = _settings.Temperature > 0 ? _settings.Temperature : 0.3f,
                AntiPrompts = new List<string> { "\n\n", "###", "---" }
            };

            var result = new StringBuilder();

            await foreach (var token in _executor.InferAsync(prompt, inferenceParams, cancellationToken))
            {
                result.Append(token);

                // Sicherheitslimit für Ausgabelänge
                if (result.Length > 2000)
                {
                    break;
                }
            }

            return result.ToString().Trim();
        }
        catch (OperationCanceledException)
        {
            return "[Generierung abgebrochen]";
        }
        catch (Exception ex)
        {
            return $"[LLM-Fehler: {ex.Message}]";
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized || _disposed)
        {
            return;
        }

        lock (_lock)
        {
            if (_initialized || _disposed)
            {
                return;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(_settings.LocalModelPath))
                {
                    _initError = "Kein Modellpfad konfiguriert (LlmSettings.LocalModelPath).";
                    return;
                }

                if (!File.Exists(_settings.LocalModelPath))
                {
                    _initError = $"Modelldatei nicht gefunden: {_settings.LocalModelPath}";
                    return;
                }

                var modelParams = new ModelParams(_settings.LocalModelPath)
                {
                    ContextSize = 2048,
                    GpuLayerCount = 0, // CPU-only für maximale Kompatibilität
                    Seed = 42
                };

                _model = LLamaWeights.LoadFromFile(modelParams);
                _context = _model.CreateContext(modelParams);
                _executor = new InteractiveExecutor(_context);
                _initialized = true;
                _initError = null;
            }
            catch (Exception ex)
            {
                _initError = $"Fehler beim Laden des Modells: {ex.Message}";
                _initialized = false;

                // Aufräumen bei Fehler
                _executor = null;
                _context?.Dispose();
                _context = null;
                _model?.Dispose();
                _model = null;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _executor = null;
            _context?.Dispose();
            _context = null;
            _model?.Dispose();
            _model = null;
        }
    }
}