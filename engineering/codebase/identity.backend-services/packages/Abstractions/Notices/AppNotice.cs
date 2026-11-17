using System.Text.Json.Serialization;

namespace WoW.Two.Sdk.Beta.Be.Identity.Notices;

/// <summary>One app notice to one person (<see cref="IAppNoticeBroker"/>), and the body of <c>POST /api/identity/v1/notices</c>.</summary>
public sealed record AppNotice
{
    /// <summary>The person's <c>sub</c> at the instance: their user id, a uuid.</summary>
    [JsonPropertyName("sub")]
    public required string Subject { get; init; }

    /// <summary>
    /// The text in Telegram's HTML subset (<c>b</c>, <c>strong</c>, <c>i</c>, <c>em</c>, <c>u</c>, <c>ins</c>, <c>s</c>,
    /// <c>strike</c>, <c>del</c>, <c>span class="tg-spoiler"</c>, <c>tg-spoiler</c>, <c>a href</c>, <c>tg-emoji</c>,
    /// <c>code</c>, <c>pre</c>, <c>blockquote</c>), with <c>&amp;lt;</c>, <c>&amp;gt;</c>, <c>&amp;amp;</c> and
    /// <c>&amp;quot;</c> for the characters outside tags; at most <see cref="AppNoticeConstants.MaxTextLength"/> characters as sent.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>Link buttons under the text, one per row, at most <see cref="AppNoticeConstants.MaxButtons"/>; empty for none.</summary>
    public IReadOnlyList<AppNoticeButton> Buttons { get; init; } = [];

    /// <summary>
    /// The caller's key for this notice, 1 to <see cref="AppNoticeConstants.MaxIdempotencyKeyLength"/> printable ASCII
    /// characters, unique per app: a repeat answers the first outcome and sends nothing more.
    /// </summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>The person's language, such as <c>uz-Latn</c>, <c>ru</c> or <c>en</c>, for the bot's own words on the notice (its mute button); null takes the bot's default.</summary>
    public string? Culture { get; init; }
}
