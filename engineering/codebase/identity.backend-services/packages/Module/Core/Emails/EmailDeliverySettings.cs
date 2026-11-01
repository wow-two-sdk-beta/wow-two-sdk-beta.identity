using WoW.Two.Sdk.Beta.Be.Comms.Email.MailKit;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Emails;

/// <summary>Holds how the instance sends email: the sender and the SMTP relay (<c>Identity:Email</c>).</summary>
/// <remarks>
/// Without <c>Smtp:Host</c> the instance still runs: every send is skipped and logged. With it, <c>From</c> is required,
/// since the account emails name no sender of their own.
/// </remarks>
public sealed record EmailDeliverySettings
{
    /// <summary>The configuration section the settings bind from.</summary>
    public const string SectionName = "Identity:Email";

    /// <summary>Gets the sender address of every account email and code, such as <c>no-reply@id.example</c>.</summary>
    public string? From { get; init; }

    /// <summary>Gets the sender's display name, such as <c>Ocharo</c>.</summary>
    public string? FromName { get; init; }

    /// <summary>Gets the SMTP relay: host, port, TLS mode (<c>None</c>, <c>Auto</c>, <c>SslOnConnect</c>, <c>StartTls</c>, <c>StartTlsWhenAvailable</c>), user name and password.</summary>
    public MailKitEmailOptions Smtp { get; init; } = new();
}
