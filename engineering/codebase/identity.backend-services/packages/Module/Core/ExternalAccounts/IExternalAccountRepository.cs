namespace WoW.Two.Sdk.Beta.Be.Identity.Core.ExternalAccounts;

/// <summary>Defines external-account persistence: which provider account belongs to which user.</summary>
public interface IExternalAccountRepository
{
    /// <summary>Link a provider account to a user.</summary>
    /// <param name="account">The external account row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAsync(IdentityExternalAccount account, CancellationToken cancellationToken = default);

    /// <summary>Unlink a provider account; returns whether one was removed.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="provider">The provider.</param>
    /// <param name="providerKey">The user's key at the provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> RemoveAsync(Guid userId, string provider, string providerKey, CancellationToken cancellationToken = default);

    /// <summary>The external account row for a provider account, or null.</summary>
    /// <param name="provider">The provider.</param>
    /// <param name="providerKey">The user's key at the provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IdentityExternalAccount?> FindAsync(string provider, string providerKey, CancellationToken cancellationToken = default);

    /// <summary>The user's linked provider accounts.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<IdentityExternalAccount>> GetAsync(Guid userId, CancellationToken cancellationToken = default);
}
