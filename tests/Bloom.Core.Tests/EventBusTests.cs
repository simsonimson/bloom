using Bloom.Core.Events;

namespace Bloom.Core.Tests;

public class EventBusTests
{
    private sealed record Ping(int Value);
    private sealed record Other;

    [Fact]
    public void Delivers_to_all_subscribers_of_the_type()
    {
        var bus = new EventBus();
        var received = new List<int>();
        bus.Subscribe<Ping>(p => received.Add(p.Value));
        bus.Subscribe<Ping>(p => received.Add(p.Value * 10));
        bus.Subscribe<Other>(_ => received.Add(-1));

        bus.Publish(new Ping(2));

        Assert.Equal([2, 20], received);
    }

    [Fact]
    public void Unsubscribe_stops_delivery()
    {
        var bus = new EventBus();
        var count = 0;
        var token = bus.Subscribe<Ping>(_ => count++);
        bus.Publish(new Ping(1));
        token.Dispose();
        token.Dispose(); // idempotent
        bus.Publish(new Ping(1));

        Assert.Equal(1, count);
    }

    [Fact]
    public void Failing_handler_does_not_stop_others_and_is_reported()
    {
        var errors = new List<Type>();
        var bus = new EventBus((_, type) => errors.Add(type));
        var delivered = false;
        bus.Subscribe<Ping>(_ => throw new InvalidOperationException());
        bus.Subscribe<Ping>(_ => delivered = true);

        bus.Publish(new Ping(1));

        Assert.True(delivered);
        Assert.Equal([typeof(Ping)], errors);
    }

    [Fact]
    public void Publish_without_subscribers_is_a_no_op()
    {
        new EventBus().Publish(new Ping(1));
    }
}
