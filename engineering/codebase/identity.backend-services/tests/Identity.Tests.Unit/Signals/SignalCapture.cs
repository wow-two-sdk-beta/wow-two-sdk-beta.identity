using System.Collections.Concurrent;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Signals;
using WoW.Two.Sdk.Beta.Be.Messaging;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Signals;

/// <summary>
/// A handler that records what reached the bus. Registered after the package's own handlers, it runs after them, so once it holds a
/// signal they have handled it too.
/// </summary>
internal sealed class SignalCapture : IEventHandler<SessionEndedEvent>, IEventHandler<AccessChangedEvent>
{
    public ConcurrentQueue<SessionEndedEvent> Sessions { get; } = new();

    public ConcurrentQueue<AccessChangedEvent> Accesses { get; } = new();

    public ValueTask HandleAsync(EventContext<SessionEndedEvent> context, CancellationToken cancellationToken)
    {
        Sessions.Enqueue(context.Event);
        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(EventContext<AccessChangedEvent> context, CancellationToken cancellationToken)
    {
        Accesses.Enqueue(context.Event);
        return ValueTask.CompletedTask;
    }

    /// <summary>Waits for a condition the bus reaches off the request.</summary>
    /// <param name="condition">The condition.</param>
    /// <param name="because">What it means when it never holds.</param>
    public static async Task UntilAsync(Func<bool> condition, string because)
        => await UntilAsync(() => Task.FromResult(condition()), because);

    /// <summary>Waits for a condition the bus reaches off the request.</summary>
    /// <param name="condition">The condition.</param>
    /// <param name="because">What it means when it never holds.</param>
    public static async Task UntilAsync(Func<Task<bool>> condition, string because)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!await condition())
        {
            if (timeout.IsCancellationRequested)
                false.Should().BeTrue(because);

            await Task.Delay(TimeSpan.FromMilliseconds(10), CancellationToken.None);
        }
    }
}
