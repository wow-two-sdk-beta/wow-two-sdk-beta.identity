namespace WoW.Two.Sdk.Beta.Be.Identity.Signals;

/// <summary>
/// Defines the record of tokens that stopped counting before they expired, keyed by issuer. Replicas sharing the store share the
/// list. Registered by <c>AddTokenDenyList</c>, which also refuses matching bearer tokens.
/// </summary>
public interface ITokenDenyList
{
    /// <summary>Denies what a session issued, whatever its <c>iat</c>.</summary>
    /// <param name="issuer">The token issuer.</param>
    /// <param name="sessionId">The session's <c>sid</c>.</param>
    /// <param name="until">When the last of its tokens has expired; null uses <see cref="DenyListOptions.DefaultEntryLifetime"/>. Already past, nothing is kept.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddSessionAsync(string issuer, string sessionId, DateTimeOffset? until, CancellationToken cancellationToken = default);

    /// <summary>Denies a person's tokens for an organization issued before <paramref name="notBefore"/>; a repeated or late signal never moves it back.</summary>
    /// <param name="issuer">The token issuer.</param>
    /// <param name="subject">The person's <c>sub</c>.</param>
    /// <param name="organizationId">The organization's <c>org_id</c>.</param>
    /// <param name="notBefore">When the access changed; a token issued from then on passes.</param>
    /// <param name="until">When the last token from before has expired; null uses <see cref="DenyListOptions.DefaultEntryLifetime"/>. Already past, nothing is kept.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAccessAsync(string issuer, string subject, string organizationId, DateTimeOffset notBefore, DateTimeOffset? until, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a token is denied: its session matches, or its person and organization match and it was issued before the change.
    /// A token with no <paramref name="issuedAt"/> counts as old.
    /// </summary>
    /// <param name="issuer">The token's <c>iss</c>.</param>
    /// <param name="sessionId">The token's <c>sid</c>, when it has one.</param>
    /// <param name="subject">The token's <c>sub</c>, when it has one.</param>
    /// <param name="organizationId">The token's <c>org_id</c>, when it has one.</param>
    /// <param name="issuedAt">The token's <c>iat</c>, when it has one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> IsDeniedAsync(string issuer, string? sessionId, string? subject, string? organizationId, DateTimeOffset? issuedAt, CancellationToken cancellationToken = default);
}
