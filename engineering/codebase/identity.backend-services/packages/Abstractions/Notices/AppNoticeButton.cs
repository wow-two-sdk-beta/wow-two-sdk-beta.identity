namespace WoW.Two.Sdk.Beta.Be.Identity.Notices;

/// <summary>A link button under an app notice: a press opens <see cref="Url"/>. Notices take no callback buttons of an app's own.</summary>
public sealed record AppNoticeButton
{
    /// <summary>The label, 1 to <see cref="AppNoticeConstants.MaxButtonTextLength"/> characters.</summary>
    public required string Text { get; init; }

    /// <summary>The absolute <c>https</c> or <c>http</c> address it opens, at most <see cref="AppNoticeConstants.MaxUrlLength"/> characters.</summary>
    public required string Url { get; init; }
}
