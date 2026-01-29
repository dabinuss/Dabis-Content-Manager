using System.Timers;
using DCM.Core.Logging;

namespace DCM.Core.Configuration;

/// <summary>
/// Verwaltet das automatische Speichern von Einstellungen mit Debouncing.
/// </summary>
public sealed class AutoSaveManager : IDisposable
{
    private readonly ObservableAppSettings _settings;
    private readonly ISettingsProvider _settingsProvider;
    private readonly IAppLogger _logger;
    private readonly System.Timers.Timer _debounceTimer;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    private bool _isDirty;
    private bool _isDisposed;
    private bool _isSuspended;
    private long _saveVersion;
    private long _lastSavedVersion;

    /// <summary>
    /// Verzögerung in Millisekunden bevor automatisch gespeichert wird.
    /// </summary>
    public int DebounceDelayMs { get; set; } = 2000;

    /// <summary>
    /// Event das nach erfolgreichem Speichern ausgelöst wird.
    /// </summary>
    public event EventHandler? SettingsSaved;

    /// <summary>
    /// Event das bei einem Speicherfehler ausgelöst wird.
    /// </summary>
    public event EventHandler<Exception>? SaveError;

    public AutoSaveManager(
        ObservableAppSettings settings,
        ISettingsProvider settingsProvider,
        IAppLogger? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        _logger = logger ?? AppLogger.Instance;

        _debounceTimer = new System.Timers.Timer(DebounceDelayMs);
        _debounceTimer.Elapsed += DebounceTimer_Elapsed;
        _debounceTimer.AutoReset = false;

        // Auf Änderungen hören
        _settings.SettingsChanged += Settings_SettingsChanged;
    }

    /// <summary>
    /// Gibt an, ob ungespeicherte Änderungen vorhanden sind.
    /// </summary>
    public bool HasUnsavedChanges => _isDirty;

    /// <summary>
    /// Suspendiert das automatische Speichern temporär.
    /// Nützlich während komplexer Operationen.
    /// </summary>
    public void Suspend()
    {
        _isSuspended = true;
        _debounceTimer.Stop();
    }

    /// <summary>
    /// Setzt das automatische Speichern fort.
    /// Falls während der Suspendierung Änderungen auftraten, wird gespeichert.
    /// </summary>
    public void Resume()
    {
        _isSuspended = false;
        if (_isDirty)
        {
            ScheduleSave();
        }
    }

    private void Settings_SettingsChanged(object? sender, EventArgs e)
    {
        if (_isDisposed) return;

        _isDirty = true;
        Interlocked.Increment(ref _saveVersion);

        if (!_isSuspended)
        {
            ScheduleSave();
        }
    }

    private void ScheduleSave()
    {
        if (_isDisposed) return;

        _debounceTimer.Stop();
        _debounceTimer.Interval = DebounceDelayMs;
        _debounceTimer.Start();
    }

    private async void DebounceTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        await SaveAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Speichert die Einstellungen sofort (synchron, blockierend).
    /// Verwendet für App-Shutdown.
    /// </summary>
    public void SaveImmediate()
    {
        if (_isDisposed) return;

        _debounceTimer.Stop();

        var acquired = false;
        try
        {
            _saveLock.Wait(TimeSpan.FromSeconds(5));
            acquired = true;

            var currentVersion = Volatile.Read(ref _saveVersion);
            if (currentVersion <= _lastSavedVersion && !_isDirty)
            {
                return;
            }

            var snapshot = _settings.ToAppSettings();
            _settingsProvider.Save(snapshot);

            _isDirty = false;
            Interlocked.Exchange(ref _lastSavedVersion, currentVersion);

            _logger.Debug("Einstellungen synchron gespeichert", "AutoSaveManager");
            SettingsSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.Error($"Fehler beim synchronen Speichern: {ex.Message}", "AutoSaveManager", ex);
            SaveError?.Invoke(this, ex);
        }
        finally
        {
            if (acquired)
            {
                _saveLock.Release();
            }
        }
    }

    /// <summary>
    /// Speichert die Einstellungen asynchron.
    /// </summary>
    public async Task SaveAsync()
    {
        if (_isDisposed) return;

        _debounceTimer.Stop();

        var acquired = false;
        try
        {
            await _saveLock.WaitAsync().ConfigureAwait(false);
            acquired = true;

            var currentVersion = Volatile.Read(ref _saveVersion);
            if (currentVersion <= _lastSavedVersion && !_isDirty)
            {
                return;
            }

            var snapshot = _settings.ToAppSettings();

            await Task.Run(() => _settingsProvider.Save(snapshot)).ConfigureAwait(false);

            _isDirty = false;
            Interlocked.Exchange(ref _lastSavedVersion, currentVersion);

            _logger.Debug("Einstellungen asynchron gespeichert", "AutoSaveManager");
            SettingsSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.Error($"Fehler beim asynchronen Speichern: {ex.Message}", "AutoSaveManager", ex);
            SaveError?.Invoke(this, ex);
        }
        finally
        {
            if (acquired)
            {
                _saveLock.Release();
            }
        }
    }

    /// <summary>
    /// Markiert die Einstellungen als geändert und plant eine Speicherung.
    /// Nützlich für manuelle Änderungen die nicht über Properties laufen.
    /// </summary>
    public void MarkDirty()
    {
        _isDirty = true;
        Interlocked.Increment(ref _saveVersion);

        if (!_isSuspended)
        {
            ScheduleSave();
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _settings.SettingsChanged -= Settings_SettingsChanged;
        _debounceTimer.Stop();
        _debounceTimer.Elapsed -= DebounceTimer_Elapsed;
        _debounceTimer.Dispose();

        // Letzte Änderungen speichern
        if (_isDirty)
        {
            SaveImmediate();
        }

        _saveLock.Dispose();
    }
}
