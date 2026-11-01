using System.Collections.Concurrent;
using System.Globalization;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TestMode;

/// <summary>
/// Keeps the messages the instance sends while test mode is on, newest 1000, in memory: nothing is written anywhere, and a
/// restart empties it. <c>GET /api/test/messages</c> reads it.
/// </summary>
/// <param name="time">Stamps each message.</param>
public sealed class TestMessageSink(TimeProvider time)
{
    /// <summary>How many messages the sink keeps; older ones drop.</summary>
    public const int Capacity = 1000;

    private readonly ConcurrentQueue<TestMessage> _messages = new();

    /// <summary>Keeps one message.</summary>
    /// <param name="channel">The OTP channel name.</param>
    /// <param name="to">The recipient as the channel addressed it.</param>
    /// <param name="subject">The subject line, for email.</param>
    /// <param name="text">The message text.</param>
    public void Add(string channel, string to, string? subject, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(text);

        _messages.Enqueue(new TestMessage { Channel = channel, To = to, Subject = subject, Text = text, SentAt = time.GetUtcNow() });
        while (_messages.Count > Capacity && _messages.TryDequeue(out _))
        {
        }
    }

    /// <summary>The kept messages, oldest first.</summary>
    public IReadOnlyList<TestMessage> Snapshot() => [.. _messages];

    /// <summary>Forgets every message.</summary>
    public void Clear() => _messages.Clear();

    /// <summary>A Telegram chat id as the sink stores it.</summary>
    /// <param name="chatId">The chat.</param>
    internal static string ChatAddress(long chatId) => chatId.ToString(CultureInfo.InvariantCulture);
}
