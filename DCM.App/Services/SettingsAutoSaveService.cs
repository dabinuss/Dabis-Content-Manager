using System;
using System.Windows.Threading;
using DCM.Core.Configuration;
using DCM.Core.Logging;

namespace DCM.App.Services;

/// <summary>
/// Zentraler Service für automatisches Speichern von Einstellungen mit Debouncing.
/// Wird von Views benachrichtigt wenn sich Einstellungen ändern.
/// </summary>
public sealed class SettingsAutoSaveService : IDisposable
{
    private readonly Func<AppSettings> _getSettings;
    private readonly Action<AppSettings> _saveSettings;
    private readonly IAppLogger _logger;
    private readonly DispatcherTimer _debounceTimer;
    private readonly object _lock = new();

    private bool _isDirty;
    private bool _isDisposed;
    private bool _isSuspended;

    /// <summary>
    /// Verzögerung in Sekunden bevor automatisch gespeichert wird.
    /// </summary>
    public double DebounceDelaySeconds { get; set; } = 2.0;

    /// <summary>
    /// Event das nach erfolgreichem Speichern ausgelöst wird.
    /// </summary>
    public event EventHandler? SettingsSaved;

    /// <summary>
    /// Event das bei einem Speicherfehler ausgelöst wird.
    /// </summary>
    public event EventHandler<Exception>? SaveError;

    public SettingsAutoSaveService(
        Func<AppSettings> getSettings,
        Action<AppSettings> saveSettings,
        Dispatcher dispatcher,
        IAppLogger? logger = null)
    {
        _getSettings = getSettings ?? throw new ArgumentNullException(nameof(getSettings));
        _saveSettings = saveSettings ?? throw new ArgumentNullException(nameof(saveSettings));
        _logger = logger ?? AppLogger.Instance;

        _debounceTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromSeconds(DebounceDelaySeconds)
        };
        _debounceTimer.Tick += DebounceTimer_Tick;
    }

    /// <summary>
    /// Gibt an, ob ungespeicherte Änderungen vorhanden sind.
    /// </summary>
    public bool HasUnsavedChanges
    {
        get
        {
            lock (_lock)
            {
                return _isDirty;
            }
        }
    }

    /// <summary>
    /// Benachrichtigt den Service, dass sich Einstellungen geändert haben.
    /// Startet den Debounce-Timer für automatisches Speichern.
    /// </summary>
    public void NotifySettingsChanged()
    {
        if (_isDisposed) return;

        lock (_lock)
        {
            _isDirty = true;
        }

        if (!_isSuspended)
        {
            ScheduleSave();
        }
    }

    /// <summary>
    /// Suspendiert das automatische Speichern temporär.
    /// Nützlich während komplexer Operationen oder beim Laden.
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
        bool shouldSave;
        lock (_lock)
        {
            shouldSave = _isDirty;
        }

        if (shouldSave)
        {
            ScheduleSave();
        }
    }

    private void ScheduleSave()
    {
        if (_isDisposed) return;

        _debounceTimer.Stop();
        _debounceTimer.Interval = TimeSpan.FromSeconds(DebounceDelaySeconds);
        _debounceTimer.Start();
    }

    private void DebounceTimer_Tick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        SaveNow();
    }

    /// <summary>
    /// Speichert die Einstellungen sofort ohne Debouncing.
    /// Nützlich vor dem Schließen der App oder nach wichtigen Aktionen.
    /// </summary>
    public void SaveNow()
    {
        if (_isDisposed) return;

        _debounceTimer.Stop();

        bool wasDirty;
        lock (_lock)
        {
            wasDirty = _isDirty;
            _isDirty = false;
        }

        if (!wasDirty) return;

        try
        {
            var settings = _getSettings();
            _saveSettings(settings);

            _logger.Debug("Einstellungen automatisch gespeichert", "AutoSave");
            SettingsSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.Error($"Fehler beim automatischen Speichern: {ex.Message}", "AutoSave", ex);
            SaveError?.Invoke(this, ex);

            // Bei Fehler als weiterhin dirty markieren
            lock (_lock)
            {
                _isDirty = true;
            }
        }
    }

    /// <summary>
    /// Verwirft alle ausstehenden Änderungen ohne zu speichern.
    /// </summary>
    public void DiscardPendingChanges()
    {
        _debounceTimer.Stop();
        lock (_lock)
        {
            _isDirty = false;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _debounceTimer.Stop();
        _debounceTimer.Tick -= DebounceTimer_Tick;

        // Letzte Änderungen speichern
        bool shouldSave;
        lock (_lock)
        {
            shouldSave = _isDirty;
            _isDirty = false;
        }

        if (shouldSave)
        {
            try
            {
                var settings = _getSettings();
                _saveSettings(settings);
            }
            catch
            {
                // Beim Dispose ignorieren
            }
        }
    }
}
