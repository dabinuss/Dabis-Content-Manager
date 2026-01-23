using System.Threading;
using System.Windows.Threading;

namespace DCM.App.Infrastructure;

internal sealed class UiThrottledAction : IDisposable
{
    private readonly UiDispatcher _ui;
    private readonly TimeSpan _interval;
    private readonly DispatcherPriority _priority;
    private readonly object _gate = new();
    private Timer? _timer;
    private Action? _pending;
    private bool _isDisposed;

    public UiThrottledAction(UiDispatcher ui, TimeSpan interval, DispatcherPriority priority)
    {
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _interval = interval;
        _priority = priority;
    }

    public void Post(Action action)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _pending = action;
            _timer ??= new Timer(_ => Flush(), null, _interval, Timeout.InfiniteTimeSpan);
        }
    }

    public void FlushNow()
    {
        Action? pending;
        lock (_gate)
        {
            pending = _pending;
            _pending = null;
            _timer?.Dispose();
            _timer = null;
        }

        if (pending is null)
        {
            return;
        }

        try
        {
            _ui.Run(pending, _priority);
        }
        catch
        {
            // Dispatcher might be shutting down.
        }
    }

    public void CancelPending()
    {
        lock (_gate)
        {
            _pending = null;
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void Flush()
    {
        Action? pending;
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            pending = _pending;
            _pending = null;
            _timer?.Dispose();
            _timer = null;
        }

        if (pending is null)
        {
            return;
        }

        try
        {
            _ui.Run(pending, _priority);
        }
        catch
        {
            // Dispatcher might be shutting down.
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _pending = null;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
