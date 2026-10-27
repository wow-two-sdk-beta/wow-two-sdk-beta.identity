namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;

/// <summary>
/// Defines what else ends with a session: the OIDC server registers one that revokes the authorizations and tokens
/// issued under it, so its refresh chains die with it. Every registered handler runs after the rows are marked revoked.
/// </summary>
public interface ISessionRevocationHandler
{
    /// <summary>End what the revoked sessions issued.</summary>
    /// <param name="sessions">The sessions just revoked, with their person and reason.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task HandleAsync(IReadOnlyList<IdentitySession> sessions, CancellationToken cancellationToken = default);
}
