using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace DCM.App.Infrastructure;

public sealed class UiEventAggregator
{
    public static UiEventAggregator Instance { get; } = new();

    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();

    private UiEventAggregator()
    {
    }

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
    {
        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        var list = _handlers.GetOrAdd(typeof(TEvent), _ => new List<Delegate>());
        lock (list)
        {
            list.Add(handler);
        }

        return new Subscription(() =>
        {
            lock (list)
            {
                list.Remove(handler);
            }
        });
    }

    public void Publish<TEvent>(TEvent eventData)
    {
        if (_handlers.TryGetValue(typeof(TEvent), out var list))
        {
            Delegate[] snapshot;
            lock (list)
            {
                snapshot = list.ToArray();
            }

            foreach (var handler in snapshot)
            {
                ((Action<TEvent>)handler)(eventData);
            }
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action _disposeAction;
        private bool _disposed;

        public Subscription(Action disposeAction)
        {
            _disposeAction = disposeAction;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _disposeAction();
        }
    }
}
