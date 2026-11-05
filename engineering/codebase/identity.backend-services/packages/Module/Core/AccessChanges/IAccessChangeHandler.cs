using WoW.Two.Sdk.Beta.Be.Identity.Core.Signals;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.AccessChanges;

/// <summary>
/// Defines what else happens when a person's access to an organization changes, as <see cref="Sessions.ISessionRevocationHandler"/>
/// does for an ended session: the OIDC server registers one that signals the services holding their tokens. Every registered
/// handler runs after the change is saved.
/// </summary>
public interface IAccessChangeHandler
{
    /// <summary>Tell what changed.</summary>
    /// <param name="change">The change.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task HandleAsync(AccessChangedEvent change, CancellationToken cancellationToken = default);
}
