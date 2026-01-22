using System.Text;
using System.Threading;
using DCM.Core.Configuration;
using DCM.Core.Logging;
using DCM.Core.Services;
using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;

namespace DCM.Llm;

public sealed class LocalLlmClient : ILlmClient, IDisposable
{
    private readonly LlmSettings _settings;
    private readonly IAppLogger _logger;
    private readonly object _lock = new();
    private readonly LlmModelType _detectedModelType;

    // Konservativ: schützt die Inferenz vor parallelen Runs (StatelessExecutor ist nicht garantiert thread-safe)
    private readonly SemaphoreSlim _inferenceGate = new(1, 1);

    private LLamaWeights? _model;
    private ModelParams? _modelParams;
    private StatelessExecutor? _executor;
    private bool _initialized;
    private bool _initializationAttempted;

    // Wichtig: NICHT volatile, damit Volatile.Read/Write keine CS0420 Warnung erzeugen.
    private bool _disposed;

    private string? _initError;

    private const long MinimumModelSizeBytes = 50 * 1024 * 1024;
    private static readonly byte[] GgufMagic = { 0x47, 0x47, 0x55, 0x46 };

    // Native Backend: retry-fähig (Flag erst nach Erfolg setzen)
    private static readonly object _nativeBackendLock = new();
    private static int _nativeBackendConfigured; // 0 = false, 1 = true

    #region Model Profiles

    private static class ModelProfiles
    {
        public static class Phi3
        {
            // Kein List<string>-Neubau pro Call -> weniger GC
            public static readonly string[] AntiPrompts =
            {
                "<|end|>",
                "<|user|>",
                "<|endoftext|>",
                "\n\n\n"
            };

            public static string FormatPrompt(string? systemPrompt, string userPrompt)
            {
                if (!string.IsNullOrWhiteSpace(systemPrompt))
                {
                    return $"<|system|>\n{systemPrompt}<|end|>\n<|user|>\n{userPrompt}<|end|>\n<|assistant|>\n";
                }
                return $"<|user|>\n{userPrompt}<|end|>\n<|assistant|>\n";
            }
        }

        public static class Mistral3
        {
            public static readonly string[] AntiPrompts =
            {
                "</s>",
                "[INST]"
            };

            public static string FormatPrompt(string? systemPrompt, string userPrompt)
            {
                if (!string.IsNullOrWhiteSpace(systemPrompt))
                {
                    return $"[SYSTEM_PROMPT]{systemPrompt}[/SYSTEM_PROMPT][INST] {userPrompt} [/INST]";
                }
                return $"[INST] {userPrompt} [/INST]";
            }
        }
    }

    #endregion

    public LocalLlmClient(LlmSettings settings, IAppLogger? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? AppLogger.Instance;
        ConfigureNativeBackend();
        _detectedModelType = DetectModelType();
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

    #region Model Type Detection

    private LlmModelType DetectModelType()
    {
        if (_settings.ModelType != LlmModelType.Auto)
        {
            _logger.Info($"Verwende konfiguriertes Modellprofil: {_settings.ModelType}", "LocalLlm");
            return _settings.ModelType;
        }

        if (string.IsNullOrWhiteSpace(_settings.LocalModelPath))
        {
            _logger.Debug("Kein Modellpfad für Auto-Erkennung, verwende Phi3 als Standard", "LocalLlm");
            return LlmModelType.Phi3;
        }

        var fileName = Path.GetFileName(_settings.LocalModelPath).ToLowerInvariant();

        if (fileName.Contains("mistral") || fileName.Contains("ministral"))
        {
            _logger.Info($"Modelltyp automatisch erkannt: Mistral3 (Dateiname: {fileName})", "LocalLlm");
            return LlmModelType.Mistral3;
        }

        if (fileName.Contains("phi"))
        {
            _logger.Info($"Modelltyp automatisch erkannt: Phi3 (Dateiname: {fileName})", "LocalLlm");
            return LlmModelType.Phi3;
        }

        _logger.Warning($"Modelltyp konnte nicht erkannt werden (Dateiname: {fileName}), verwende Phi3 als Standard", "LocalLlm");
        return LlmModelType.Phi3;
    }

    private string[] GetAntiPrompts()
    {
        return _detectedModelType switch
        {
            LlmModelType.Mistral3 => ModelProfiles.Mistral3.AntiPrompts,
            LlmModelType.Phi3 => ModelProfiles.Phi3.AntiPrompts,
            _ => ModelProfiles.Phi3.AntiPrompts
        };
    }

    private string FormatPrompt(string userPrompt)
    {
        var systemPrompt = _settings.SystemPrompt;

        return _detectedModelType switch
        {
            LlmModelType.Mistral3 => ModelProfiles.Mistral3.FormatPrompt(systemPrompt, userPrompt),
            LlmModelType.Phi3 => ModelProfiles.Phi3.FormatPrompt(systemPrompt, userPrompt),
            _ => ModelProfiles.Phi3.FormatPrompt(systemPrompt, userPrompt)
        };
    }

    #endregion

    private void ConfigureNativeBackend()
    {
        if (Volatile.Read(ref _nativeBackendConfigured) == 1)
        {
            return;
        }

        lock (_nativeBackendLock)
        {
            if (_nativeBackendConfigured == 1)
            {
                return;
            }

            try
            {
                var baseDir = AppContext.BaseDirectory;
                var runtimeDir = Path.Combine(baseDir, "runtimes");

                NativeLibraryConfig.All.WithSearchDirectory(baseDir);
                NativeLibraryConfig.All.WithSearchDirectory(runtimeDir);
                NativeLibraryConfig.All.WithAutoFallback(true);

                Volatile.Write(ref _nativeBackendConfigured, 1);

                _logger.Debug($"Native Backend-Suche konfiguriert (Base: {baseDir}, Runtimes: {runtimeDir})", "LocalLlm");
            }
            catch (Exception ex)
            {
                // Flag bleibt 0 -> erneuter Versuch bei nächster Instanz möglich
                _logger.Warning($"Native Backend-Konfiguration fehlgeschlagen, verwende Standard: {ex.Message}", "LocalLlm");
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

    private static int GetGpuLayerCount()
    {
        // Default beibehalten (Kompat), aber optional per Env überschreibbar
        const int defaultLayers = 35;

        try
        {
            var raw = Environment.GetEnvironmentVariable("DCM_LLM_GPU_LAYERS");
            if (!string.IsNullOrWhiteSpace(raw)
                && int.TryParse(raw, out var parsed)
                && parsed >= 0
                && parsed <= 200)
            {
                return parsed;
            }
        }
        catch
        {
            // Ignorieren -> Default
        }

        return defaultLayers;
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
            _logger.Info($"Lade LLM-Modell: {_settings.LocalModelPath} (Typ: {_detectedModelType})", "LocalLlm");

            var contextSize = _settings.ContextSize > 0 ? (uint)_settings.ContextSize : 4096u;
            var gpuLayerCount = GetGpuLayerCount();

            _modelParams = new ModelParams(_settings.LocalModelPath!)
            {
                ContextSize = contextSize,
                GpuLayerCount = gpuLayerCount,
                Threads = Math.Max(1, Environment.ProcessorCount / 2)
            };

            _logger.Debug($"ModelParams erstellt (ContextSize: {contextSize}, GpuLayerCount: {gpuLayerCount}), lade Weights...", "LocalLlm");

            _model = LLamaWeights.LoadFromFile(_modelParams);

            _logger.Debug("Weights geladen, erstelle StatelessExecutor...", "LocalLlm");

            _executor = new StatelessExecutor(_model, _modelParams);

            _logger.Info($"LLM-Modell erfolgreich geladen (Profil: {_detectedModelType})", "LocalLlm");

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

    // Optionaler interner Reset (kein Interface-Bruch). Kann in kontrollierten Flows genutzt werden.
    // Aktuell bewusst NICHT aufgerufen, um bestehendes Verhalten nicht zu ändern.
    private void ResetInitializationState()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _initialized = false;
            _initializationAttempted = false;
            _initError = null;

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

            ValidateModelPath();
        }
    }

    public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return string.Empty;
        }

        StatelessExecutor executor;
        InferenceParams inferenceParams;
        string formattedPrompt;
        int tokenCountLimit;
        const int charLimit = 4000;

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

            // Prompt formatieren mit Modellprofil
            formattedPrompt = FormatPrompt(prompt);

            // 0 ist gültig (deterministisch). Negativ oder NaN -> Fallback.
            var temperature = _settings.Temperature;
            if (float.IsNaN(temperature) || temperature < 0f)
            {
                temperature = 0.7f;
            }

            // Token-Limit konsistent: InferenceParams.MaxTokens ist die Quelle der Wahrheit.
            var maxTokens = _settings.MaxTokens;
            if (maxTokens <= 0)
            {
                maxTokens = 256;
            }
            maxTokens = Math.Min(maxTokens, 1024);
            tokenCountLimit = maxTokens;

            var samplingPipeline = new DefaultSamplingPipeline
            {
                Temperature = temperature
            };

            inferenceParams = new InferenceParams
            {
                MaxTokens = maxTokens,
                AntiPrompts = GetAntiPrompts(),
                SamplingPipeline = samplingPipeline
            };
        }

        var gateAcquired = false;

        try
        {
            await _inferenceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            gateAcquired = true;

            // Falls zwischenzeitlich disposed wurde, nicht mehr starten (verhindert Executor-after-dispose).
            if (Volatile.Read(ref _disposed))
            {
                _logger.Warning("CompleteAsync gestartet, aber Client wurde zwischenzeitlich disposed", "LocalLlm");
                return "[LLM wurde disposed]";
            }

            _logger.Debug($"LLM-Inferenz gestartet (Profil: {_detectedModelType}), Prompt-Länge: {formattedPrompt.Length} Zeichen", "LocalLlm");

            var estimatedCapacity = Math.Min(charLimit, Math.Max(256, tokenCountLimit * 4));
            var result = new StringBuilder(estimatedCapacity);
            var tokenCount = 0;

            await foreach (var token in executor.InferAsync(formattedPrompt, inferenceParams, cancellationToken))
            {
                if (Volatile.Read(ref _disposed))
                {
                    _logger.Warning("LLM während der Generierung disposed", "LocalLlm");
                    return "[LLM wurde während der Generierung disposed]";
                }

                result.Append(token);
                tokenCount++;

                // Konsistent: Token-Limit kommt von InferenceParams.MaxTokens
                if (tokenCount >= tokenCountLimit || result.Length > charLimit)
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
        finally
        {
            if (gateAcquired)
            {
                try
                {
                    _inferenceGate.Release();
                }
                catch
                {
                    // Ignorieren
                }
            }
        }
    }

    public void Dispose()
    {
        // Markiere disposed unter Lock, damit neue Aufrufe sofort stoppen.
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _logger.Debug("LocalLlmClient wird disposed", "LocalLlm");
            Volatile.Write(ref _disposed, true);
        }

        // Warte konservativ auf laufende Inferenz, bevor wir Executor/Model freigeben.
        // (Kein await im Dispose -> sync Wait ist ok)
        var gateAcquired = false;
        try
        {
            _inferenceGate.Wait();
            gateAcquired = true;
        }
        catch
        {
            // Ignorieren
        }
        finally
        {
            if (gateAcquired)
            {
                try
                {
                    _inferenceGate.Release();
                }
                catch
                {
                    // Ignorieren
                }
            }
        }

        lock (_lock)
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

            _logger.Debug("LocalLlmClient disposed", "LocalLlm");
        }

        // SemaphoreSlim nicht disposed -> vermeidet seltene Race-Crashes bei späten Calls.
        // (Ist klein, langlebig, und der Client selbst lebt typischerweise bis App-Ende.)
    }
}
