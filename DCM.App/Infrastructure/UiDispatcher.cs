using System.Windows.Threading;

namespace DCM.App.Infrastructure;

internal sealed class UiDispatcher
{
    private readonly Dispatcher _dispatcher;

    public UiDispatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public void Run(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.BeginInvoke(action, priority);
    }

    public Task RunAsync(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        if (_dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return _dispatcher.InvokeAsync(action, priority).Task;
    }

    public Task RunAsync(Func<Task> action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        if (_dispatcher.CheckAccess())
        {
            return action();
        }

        return _dispatcher.InvokeAsync(action, priority).Task.Unwrap();
    }

    public Task<T> RunAsync<T>(Func<T> func, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        if (func is null)
        {
            throw new ArgumentNullException(nameof(func));
        }

        if (_dispatcher.CheckAccess())
        {
            return Task.FromResult(func());
        }

        return _dispatcher.InvokeAsync(func, priority).Task;
    }
}
