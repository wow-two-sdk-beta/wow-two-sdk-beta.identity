namespace WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;

/// <summary>Signs in with the code the bot sent to a phone number's chat.</summary>
public sealed record PhoneSignInApiRequest
{
    /// <summary>The number the code went to, in E.164.</summary>
    public required string Phone { get; init; }

    /// <summary>The code.</summary>
    public required string Code { get; init; }
}
