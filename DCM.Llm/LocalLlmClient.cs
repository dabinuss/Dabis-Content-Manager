using System.Text;
using DCM.Core.Configuration;
using DCM.Core.Logging;
using DCM.Core.Services;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace DCM.Llm;

public sealed class LocalLlmClient : ILlmClient, IDisposable
{
    private readonly LlmSettings _settings;
    private readonly IAppLogger _logger;
    private readonly object _lock = new();

    private LLamaWeights? _model;
    private ModelParams? _modelParams;
    private StatelessExecutor? _executor;
    private bool _initialized;
    private bool _initializationAttempted;
    private bool _disposed;
    private string? _initError;

    private const long MinimumModelSizeBytes = 50 * 1024 * 1024;
    private static readonly byte[] GgufMagic = { 0x47, 0x47, 0x55, 0x46 };

    public LocalLlmClient(LlmSettings settings, IAppLogger? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? AppLogger.Instance;
        ValidateModelPath();
    }

    public bool IsReady
    {
        get
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return false;
                }

                if (!_initializationAttempted)
                {
                    return false;
                }

                return _initialized && _executor is not null;
            }
        }
    }

    public string? InitializationError
    {
        get
        {
            lock (_lock)
            {
                return _initError;
            }
        }
    }

    private void ValidateModelPath()
    {
        if (string.IsNullOrWhiteSpace(_settings.LocalModelPath))
        {
            _initError = "Kein Modellpfad konfiguriert.";
            _logger.Warning(_initError, "LocalLlm");
            return;
        }

        if (!File.Exists(_settings.LocalModelPath))
        {
            _initError = "Modelldatei nicht gefunden.";
            _logger.Warning($"{_initError}: {_settings.LocalModelPath}", "LocalLlm");
            return;
        }

        try
        {
            var fileInfo = new FileInfo(_settings.LocalModelPath);

            if (fileInfo.Length < MinimumModelSizeBytes)
            {
                _initError = $"Datei zu klein ({fileInfo.Length / 1024 / 1024} MB).";
                _logger.Warning(_initError, "LocalLlm");
                return;
            }

            if (!IsValidGgufFile(_settings.LocalModelPath))
            {
                _initError = "Keine gültige GGUF-Datei.";
                _logger.Warning(_initError, "LocalLlm");
                return;
            }

            _initError = null;
            _logger.Debug($"Modellpfad validiert: {_settings.LocalModelPath}", "LocalLlm");
        }
        catch (Exception ex)
        {
            _initError = $"Dateizugriff fehlgeschlagen: {ex.Message}";
            _logger.Error(_initError, "LocalLlm", ex);
        }
    }

    private static bool IsValidGgufFile(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[4];
            var bytesRead = fs.Read(buffer, 0, 4);

            if (bytesRead < 4)
            {
                return false;
            }

            return buffer[0] == GgufMagic[0]
                && buffer[1] == GgufMagic[1]
                && buffer[2] == GgufMagic[2]
                && buffer[3] == GgufMagic[3];
        }
        catch
        {
            return false;
        }
    }

    public bool TryInitialize()
    {
        lock (_lock)
        {
            return TryInitializeCore();
        }
    }

    private bool TryInitializeCore()
    {
        if (_disposed)
        {
            return false;
        }

        if (_initializationAttempted)
        {
            return _initialized;
        }

        _initializationAttempted = true;

        if (!string.IsNullOrEmpty(_initError))
        {
            return false;
        }

        try
        {
            _logger.Info($"Lade LLM-Modell: {_settings.LocalModelPath}", "LocalLlm");

            _modelParams = new ModelParams(_settings.LocalModelPath!)
            {
                ContextSize = 2048,
                GpuLayerCount = 35,
                Threads = Math.Max(1, Environment.ProcessorCount / 2)
            };

            _logger.Debug("ModelParams erstellt, lade Weights...", "LocalLlm");

            _model = LLamaWeights.LoadFromFile(_modelParams);

            _logger.Debug("Weights geladen, erstelle StatelessExecutor...", "LocalLlm");

            _executor = new StatelessExecutor(_model, _modelParams);

            _logger.Info("LLM-Modell erfolgreich geladen", "LocalLlm");

            _initialized = true;
            _initError = null;
            return true;
        }
        catch (DllNotFoundException ex)
        {
            _initError = $"Native Bibliothek fehlt: {ex.Message}";
            _logger.Error(_initError, "LocalLlm", ex);
            CleanupAfterError();
            return false;
        }
        catch (BadImageFormatException ex)
        {
            _initError = "Architektur-Konflikt (32/64-bit).";
            _logger.Error(_initError, "LocalLlm", ex);
            CleanupAfterError();
            return false;
        }
        catch (Exception ex)
        {
            _initError = $"Laden fehlgeschlagen: {ex.Message}";
            _logger.Error(_initError, "LocalLlm", ex);
            CleanupAfterError();
            return false;
        }
    }

    private void CleanupAfterError()
    {
        _initialized = false;
        DisposeExecutor();
        _modelParams = null;

        try
        {
            _model?.Dispose();
        }
        catch
        {
            // Ignorieren
        }

        _model = null;
    }

    private void DisposeExecutor()
    {
        if (_executor is null)
        {
            return;
        }

        if (_executor is IDisposable disposableExecutor)
        {
            try
            {
                disposableExecutor.Dispose();
            }
            catch
            {
                // Ignorieren
            }
        }

        _executor = null;
    }

    public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return string.Empty;
        }

        StatelessExecutor executor;
        InferenceParams inferenceParams;

        lock (_lock)
        {
            if (_disposed)
            {
                _logger.Warning("CompleteAsync aufgerufen nach Dispose", "LocalLlm");
                return "[LLM wurde disposed]";
            }

            if (!_initializationAttempted)
            {
                TryInitializeCore();
            }

            if (!_initialized || _executor is null)
            {
                _logger.Warning($"LLM nicht verfügbar: {_initError ?? "Unbekannter Fehler"}", "LocalLlm");
                return $"[LLM nicht verfügbar: {_initError ?? "Unbekannter Fehler"}]";
            }

            executor = _executor;

            var samplingPipeline = new DefaultSamplingPipeline
            {
                Temperature = _settings.Temperature > 0 ? _settings.Temperature : 0.7f
            };

            inferenceParams = new InferenceParams
            {
                MaxTokens = Math.Min(_settings.MaxTokens > 0 ? _settings.MaxTokens : 256, 512),
                AntiPrompts = new List<string> { "<|end|>", "<|user|>", "<|endoftext|>", "\n\n\n" },
                SamplingPipeline = samplingPipeline
            };
        }

        try
        {
            _logger.Debug($"LLM-Inferenz gestartet, Prompt-Länge: {prompt.Length} Zeichen", "LocalLlm");

            var result = new StringBuilder();
            var tokenCount = 0;

            await foreach (var token in executor.InferAsync(prompt, inferenceParams, cancellationToken))
            {
                lock (_lock)
                {
                    if (_disposed)
                    {
                        _logger.Warning("LLM während der Generierung disposed", "LocalLlm");
                        return "[LLM wurde während der Generierung disposed]";
                    }
                }

                result.Append(token);
                tokenCount++;

                if (result.Length > 1500 || tokenCount > 400)
                {
                    _logger.Debug($"LLM-Limit erreicht: {result.Length} Zeichen, {tokenCount} Tokens", "LocalLlm");
                    break;
                }
            }

            _logger.Debug($"LLM-Inferenz abgeschlossen: {result.Length} Zeichen, {tokenCount} Tokens", "LocalLlm");

            return result.ToString().Trim();
        }
        catch (OperationCanceledException)
        {
            _logger.Debug("LLM-Inferenz abgebrochen", "LocalLlm");
            return "[Generierung abgebrochen]";
        }
        catch (Exception ex)
        {
            _logger.Error($"LLM-Inferenz fehlgeschlagen: {ex.Message}", "LocalLlm", ex);
            return $"[LLM-Fehler: {ex.Message}]";
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _logger.Debug("LocalLlmClient wird disposed", "LocalLlm");

            _disposed = true;
            _initialized = false;

            DisposeExecutor();
            _modelParams = null;

            try
            {
                _model?.Dispose();
            }
            catch
            {
                // Ignorieren
            }

            _model = null;

            _logger.Debug("LocalLlmClient disposed", "LocalLlm");
        }
    }
}