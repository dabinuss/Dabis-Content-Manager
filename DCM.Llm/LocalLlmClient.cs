using System.Text;
using DCM.Core.Configuration;
using DCM.Core.Services;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace DCM.Llm;

public sealed class LocalLlmClient : ILlmClient, IDisposable
{
    private readonly LlmSettings _settings;
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

    public LocalLlmClient(LlmSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ValidateModelPath();
    }

    public bool IsReady
    {
        get
        {
            lock (_lock)
            {
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
            return;
        }

        if (!File.Exists(_settings.LocalModelPath))
        {
            _initError = "Modelldatei nicht gefunden.";
            return;
        }

        try
        {
            var fileInfo = new FileInfo(_settings.LocalModelPath);

            if (fileInfo.Length < MinimumModelSizeBytes)
            {
                _initError = $"Datei zu klein ({fileInfo.Length / 1024 / 1024} MB).";
                return;
            }

            if (!IsValidGgufFile(_settings.LocalModelPath))
            {
                _initError = "Keine gültige GGUF-Datei.";
                return;
            }

            _initError = null;
        }
        catch (Exception ex)
        {
            _initError = $"Dateizugriff fehlgeschlagen: {ex.Message}";
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
                return false;

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
                System.Diagnostics.Debug.WriteLine($"[LLM] Starte Laden von: {_settings.LocalModelPath}");

                _modelParams = new ModelParams(_settings.LocalModelPath!)
                {
                    ContextSize = 2048,
                    GpuLayerCount = 35,
                    Threads = Math.Max(1, Environment.ProcessorCount / 2)
                };

                System.Diagnostics.Debug.WriteLine("[LLM] ModelParams erstellt, lade Weights...");

                _model = LLamaWeights.LoadFromFile(_modelParams);

                System.Diagnostics.Debug.WriteLine("[LLM] Weights geladen, erstelle StatelessExecutor...");

                _executor = new StatelessExecutor(_model, _modelParams);

                System.Diagnostics.Debug.WriteLine("[LLM] Executor erstellt - ERFOLG!");

                _initialized = true;
                _initError = null;
                return true;
            }
            catch (DllNotFoundException ex)
            {
                _initError = $"Native Bibliothek fehlt: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[LLM] DllNotFoundException: {ex}");
                CleanupAfterError();
                return false;
            }
            catch (BadImageFormatException ex)
            {
                _initError = "Architektur-Konflikt (32/64-bit).";
                System.Diagnostics.Debug.WriteLine($"[LLM] BadImageFormatException: {ex}");
                CleanupAfterError();
                return false;
            }
            catch (Exception ex)
            {
                _initError = $"Laden fehlgeschlagen: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[LLM] Exception: {ex}");
                CleanupAfterError();
                return false;
            }
        }
    }

    private void CleanupAfterError()
    {
        // Hinweis: Diese Methode wird innerhalb eines Locks aufgerufen
        _initialized = false;

        // StatelessExecutor disposen falls möglich
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

        // StatelessExecutor implementiert IDisposable in neueren LLamaSharp-Versionen
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

        // Thread-safe Prüfung und ggf. Initialisierung
        StatelessExecutor? executor;
        lock (_lock)
        {
            if (_disposed)
            {
                return "[LLM wurde disposed]";
            }

            if (!_initializationAttempted)
            {
                // Initialisierung innerhalb des Locks
                TryInitializeInternal();
            }

            if (!_initialized || _executor is null)
            {
                return $"[LLM nicht verfügbar: {_initError ?? "Unbekannter Fehler"}]";
            }

            executor = _executor;
        }

        try
        {
            System.Diagnostics.Debug.WriteLine($"[LLM] CompleteAsync gestartet, Prompt-Länge: {prompt.Length}");

            var samplingPipeline = new DefaultSamplingPipeline
            {
                Temperature = _settings.Temperature > 0 ? _settings.Temperature : 0.7f
            };

            var inferenceParams = new InferenceParams
            {
                MaxTokens = Math.Min(_settings.MaxTokens > 0 ? _settings.MaxTokens : 256, 512),
                AntiPrompts = new List<string> { "<|end|>", "<|user|>", "<|endoftext|>", "\n\n\n" },
                SamplingPipeline = samplingPipeline
            };

            var result = new StringBuilder();
            var tokenCount = 0;

            await foreach (var token in executor.InferAsync(prompt, inferenceParams, cancellationToken))
            {
                result.Append(token);
                tokenCount++;

                if (result.Length > 1500 || tokenCount > 400)
                {
                    System.Diagnostics.Debug.WriteLine($"[LLM] Limit erreicht: {result.Length} chars, {tokenCount} tokens");
                    break;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[LLM] CompleteAsync fertig: {result.Length} chars, {tokenCount} tokens");

            return result.ToString().Trim();
        }
        catch (OperationCanceledException)
        {
            return "[Generierung abgebrochen]";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LLM] CompleteAsync Exception: {ex}");
            return $"[LLM-Fehler: {ex.Message}]";
        }
    }

    /// <summary>
    /// Interne Initialisierung ohne erneuten Lock-Versuch.
    /// MUSS innerhalb eines Locks aufgerufen werden.
    /// </summary>
    private void TryInitializeInternal()
    {
        if (_initializationAttempted)
        {
            return;
        }

        _initializationAttempted = true;

        if (!string.IsNullOrEmpty(_initError))
        {
            return;
        }

        try
        {
            System.Diagnostics.Debug.WriteLine($"[LLM] Starte Laden von: {_settings.LocalModelPath}");

            _modelParams = new ModelParams(_settings.LocalModelPath!)
            {
                ContextSize = 2048,
                GpuLayerCount = 35,
                Threads = Math.Max(1, Environment.ProcessorCount / 2)
            };

            _model = LLamaWeights.LoadFromFile(_modelParams);
            _executor = new StatelessExecutor(_model, _modelParams);

            System.Diagnostics.Debug.WriteLine("[LLM] Executor erstellt - ERFOLG!");

            _initialized = true;
            _initError = null;
        }
        catch (Exception ex)
        {
            _initError = $"Laden fehlgeschlagen: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[LLM] Exception: {ex}");
            CleanupAfterError();
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

            _disposed = true;
            _initialized = false;

            // Executor disposen
            DisposeExecutor();

            _modelParams = null;

            // Model disposen
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
    }
}