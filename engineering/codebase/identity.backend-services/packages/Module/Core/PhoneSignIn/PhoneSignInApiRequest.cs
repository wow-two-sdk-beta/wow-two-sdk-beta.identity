namespace WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;

/// <summary>Signs in with the code the bot sent to a phone number's chat.</summary>
public sealed record PhoneSignInApiRequest
{
    /// <summary>The number the code went to, in E.164.</summary>
    public required string Phone { get; init; }

    /// <summary>The code.</summary>
    public required string Code { get; init; }

    /// <summary>
    /// Whether the person asked to stay signed in ("Keep me signed in"): a fixed session then takes
    /// <c>Identity:Sessions:ExtendedLifetime</c> and a cookie that outlives the browser; ignored while sessions slide or no
    /// extended lifetime is set (<c>sign-in-options</c>' <c>isPersistenceOffered</c>).
    /// </summary>
    public bool IsPersistent { get; init; }

    /// <summary>
    /// The token of an invitation link: the person joins its organization with its role, and a first sign-in creates them
    /// even while sign-up is by invitation only. An invitation naming a phone number takes only that number.
    /// </summary>
    public string? InvitationToken { get; init; }
}
