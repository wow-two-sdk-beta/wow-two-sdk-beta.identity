namespace WoW.Two.Sdk.Beta.Be.Identity.Jwt;

/// <summary>Finds an issuer <see cref="JwtOptions.TrustedIssuers"/> doesn't list, so a new issuer needs no restart.</summary>
/// <remarks>
/// Registered scoped through <see cref="JwtOptions.UseTrustedIssuerResolver{TResolver}"/> and resolved in its own scope. It
/// runs before the token's signature is checked, on any <c>iss</c> a caller sends, for every such request: look it up in
/// a store, cache the answer, and never reach out to an address the value names.
/// </remarks>
public interface ITrustedIssuerResolver
{
    /// <summary>Finds a trusted issuer.</summary>
    /// <param name="issuer">The token's unvalidated <c>iss</c>.</param>
    /// <param name="cancellationToken">Cancelled when the request is aborted.</param>
    /// <returns>
    /// The issuer, whose <see cref="TrustedIssuer.Issuer"/> must equal <paramref name="issuer"/>; null when it isn't trusted.
    /// </returns>
    ValueTask<TrustedIssuer?> ResolveAsync(string issuer, CancellationToken cancellationToken);
}
