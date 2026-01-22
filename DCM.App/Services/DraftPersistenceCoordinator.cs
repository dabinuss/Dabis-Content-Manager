using System;
using System.Windows.Threading;

namespace DCM.App.Services;

internal sealed class DraftPersistenceCoordinator
{
    private readonly DispatcherTimer _persistTimer;
    private readonly DispatcherTimer _debounceTimer;
    private readonly Func<bool> _canPersist;
    private readonly Func<bool> _isRestoring;
    private readonly Action _persistAction;
    private bool _dirty;
    private bool _debouncePending;

    public DraftPersistenceCoordinator(
        Dispatcher dispatcher,
        TimeSpan persistInterval,
        TimeSpan debounceDelay,
        Func<bool> canPersist,
        Func<bool> isRestoring,
        Action persistAction)
    {
        _canPersist = canPersist ?? throw new ArgumentNullException(nameof(canPersist));
        _isRestoring = isRestoring ?? throw new ArgumentNullException(nameof(isRestoring));
        _persistAction = persistAction ?? throw new ArgumentNullException(nameof(persistAction));

        _persistTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = persistInterval
        };
        _persistTimer.Tick += PersistTimer_Tick;

        _debounceTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = debounceDelay
        };
        _debounceTimer.Tick += DebounceTimer_Tick;
    }

    public bool IsDirty => _dirty || _debouncePending;

    public void Schedule()
    {
        if (ShouldSkip())
        {
            return;
        }

        _dirty = true;
        _persistTimer.Stop();
        _persistTimer.Start();
    }

    public void ScheduleDebounced()
    {
        if (ShouldSkip())
        {
            return;
        }

        _debouncePending = true;
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    public void Stop()
    {
        _persistTimer.Stop();
        _debounceTimer.Stop();
    }

    public void FlushIfDirty()
    {
        Stop();

        if (_debouncePending)
        {
            _debouncePending = false;
            _dirty = true;
        }

        if (!_dirty)
        {
            return;
        }

        _dirty = false;
        _persistAction();
    }

    private void DebounceTimer_Tick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        if (!_debouncePending)
        {
            return;
        }

        _debouncePending = false;
        Schedule();
    }

    private void PersistTimer_Tick(object? sender, EventArgs e)
    {
        _persistTimer.Stop();
        if (!_dirty)
        {
            return;
        }

        _dirty = false;
        _persistAction();
    }

    private bool ShouldSkip() => _isRestoring() || !_canPersist();
}
