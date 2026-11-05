namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Sessions;

/// <summary>
/// Represents what a session keeps server-side: the person's claims and the instance's tokens. It travels only inside a
/// <see cref="BffSessionRecord.Payload"/>, encrypted by <see cref="BffSessionProtector"/>.
/// </summary>
internal sealed record BffSessionState
{
    /// <summary>Gets the random reference of this sign-in, which the cookie carries beside the <c>sid</c>; a later sign-in
    /// under the same <c>sid</c> draws a new one, so an older cookie no longer matches.</summary>
    public required string Reference { get; init; }

    /// <summary>Gets the person's claims, from the latest ID token.</summary>
    public required IReadOnlyList<BffSessionClaim> Claims { get; init; }

    /// <summary>Gets the access token, or null when the instance issued none.</summary>
    public string? AccessToken { get; init; }

    /// <summary>Gets when the access token expires (UTC), or null when unknown; the session refreshes ahead of it.</summary>
    public DateTimeOffset? AccessTokenExpiresAt { get; init; }

    /// <summary>Gets the refresh token, or null without the <c>offline_access</c> scope.</summary>
    public string? RefreshToken { get; init; }

    /// <summary>Gets the latest ID token, sent as <c>id_token_hint</c> at end-session.</summary>
    public string? IdentityToken { get; init; }

    /// <summary>Gets when the person signed in to the BFF (UTC).</summary>
    public required DateTimeOffset SignedInAt { get; init; }

    /// <summary>Gets when <see cref="Claims"/> were last read from the instance (UTC), set at sign-in and on each refresh; null on a
    /// session stored before this existed, which counts from <see cref="SignedInAt"/>.</summary>
    public DateTimeOffset? ClaimsAsOf { get; init; }

    /// <summary>Gets the absolute end of the session (UTC), never extended by a refresh.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}
