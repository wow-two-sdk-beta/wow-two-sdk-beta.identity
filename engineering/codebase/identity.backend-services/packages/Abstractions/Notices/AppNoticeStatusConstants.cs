namespace WoW.Two.Sdk.Beta.Be.Identity.Notices;

/// <summary>Names the outcomes a caller records per notice; the first three come back as <see cref="AppNoticeDelivery.Status"/>.</summary>
public static class AppNoticeStatusConstants
{
    /// <summary>Telegram took the notice; the route answers <c>202</c> with the ledger id.</summary>
    public const string Sent = "sent";

    /// <summary>The person muted the app's notices (<c>/stop</c>, or the notice's mute button); nothing went out; the route answers <c>200</c>.</summary>
    public const string Muted = "muted";

    /// <summary>The person has no confirmed phone linked to the bot, or no such person exists; the route answers <c>404</c> <see cref="AppNoticeErrorCodeConstants.NotLinked"/>.</summary>
    public const string NotLinked = "not_linked";

    /// <summary>The caller's record of a failed send: any failure the broker returns.</summary>
    public const string Failed = "failed";
}
