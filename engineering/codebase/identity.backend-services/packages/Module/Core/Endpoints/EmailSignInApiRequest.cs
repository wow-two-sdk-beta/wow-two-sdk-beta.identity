namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

/// <summary>Signs in with the token of an emailed link or an emailed code.</summary>
public sealed record EmailSignInApiRequest
{
    /// <summary>The address the link or code went to.</summary>
    public required string Email { get; init; }

    /// <summary>The token from the link.</summary>
    public string? Token { get; init; }

    /// <summary>The code, when no token is given.</summary>
    public string? Code { get; init; }

    /// <summary>
    /// Whether the person asked to stay signed in ("Keep me signed in"): a fixed session then takes
    /// <c>Identity:Sessions:ExtendedLifetime</c> and a cookie that outlives the browser; ignored while sessions slide or no
    /// extended lifetime is set (<c>sign-in-options</c>' <c>isPersistenceOffered</c>).
    /// </summary>
    public bool IsPersistent { get; init; }
}
