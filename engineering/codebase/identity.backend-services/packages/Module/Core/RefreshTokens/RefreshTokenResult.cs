namespace WoW.Two.Sdk.Beta.Be.Identity.Core.RefreshTokens;

/// <summary>Carries a redemption's status, and on success the user and the replacement token.</summary>
public sealed record RefreshTokenResult
{
    /// <summary>The outcome.</summary>
    public required RefreshTokenStatus Status { get; init; }

    /// <summary>The token's user; set on success. Complete with <c>SignInService.SignInAsync</c> to apply lockout and preconditions.</summary>
    public IdentityUser? User { get; init; }

    /// <summary>The replacement token; set on success. The redeemed token is spent.</summary>
    public RefreshTokenModel? Token { get; init; }

    /// <summary>Whether the redemption succeeded.</summary>
    public bool Succeeded => Status == RefreshTokenStatus.Succeeded;
}
