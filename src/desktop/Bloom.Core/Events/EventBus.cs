using System.Collections.Concurrent;

namespace Bloom.Core.Events;

/// <summary>
/// In-process event bus. Modules communicate only through events and interfaces,
/// never by referencing each other directly (Blueprint 6.4).
/// </summary>
public interface IEventBus
{
    void Publish<TEvent>(TEvent message) where TEvent : notnull;

    /// <summary>Registers a handler. Dispose the returned token to unsubscribe.</summary>
    IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : notnull;
}

public sealed class EventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, ImmutableHandlerList> _handlers = new();
    private readonly Action<Exception, Type>? _onHandlerError;

    /// <param name="onHandlerError">Called when a handler throws. A failing handler never stops other handlers.</param>
    public EventBus(Action<Exception, Type>? onHandlerError = null)
    {
        _onHandlerError = onHandlerError;
    }

    public void Publish<TEvent>(TEvent message) where TEvent : notnull
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out var list)) return;
        foreach (var handler in list.Items)
        {
            try
            {
                ((Action<TEvent>)handler)(message);
            }
            catch (Exception ex)
            {
                _onHandlerError?.Invoke(ex, typeof(TEvent));
            }
        }
    }

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handlers.AddOrUpdate(typeof(TEvent),
            _ => new ImmutableHandlerList([handler]),
            (_, existing) => existing.Add(handler));
        return new Subscription(() => _handlers.AddOrUpdate(typeof(TEvent),
            _ => new ImmutableHandlerList([]),
            (_, existing) => existing.Remove(handler)));
    }

    private sealed record ImmutableHandlerList(Delegate[] Items)
    {
        public ImmutableHandlerList Add(Delegate d) => new([.. Items, d]);
        public ImmutableHandlerList Remove(Delegate d) => new(Items.Where(x => x != d).ToArray());
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
