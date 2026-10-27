namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Logins;

/// <summary>Defines external-login persistence: which provider account belongs to which user.</summary>
public interface IUserLoginRepository
{
    /// <summary>Link a provider account to a user.</summary>
    /// <param name="login">The login row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAsync(IdentityUserLogin login, CancellationToken cancellationToken = default);

    /// <summary>Unlink a provider account; returns whether one was removed.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="loginProvider">The provider.</param>
    /// <param name="providerKey">The user's key at the provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> RemoveAsync(Guid userId, string loginProvider, string providerKey, CancellationToken cancellationToken = default);

    /// <summary>The login row for a provider account, or null.</summary>
    /// <param name="loginProvider">The provider.</param>
    /// <param name="providerKey">The user's key at the provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IdentityUserLogin?> FindAsync(string loginProvider, string providerKey, CancellationToken cancellationToken = default);

    /// <summary>The user's linked provider accounts.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<IdentityUserLogin>> GetAsync(Guid userId, CancellationToken cancellationToken = default);
}
