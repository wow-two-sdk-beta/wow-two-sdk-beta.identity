namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Logins;

/// <summary>
/// Provides external logins: link a provider account (Google, Microsoft, Telegram, …) to a user, find the user behind
/// one, and unlink it. Unlinking rotates the security stamp. The OAuth handshake itself stays with the OAuth packages.
/// </summary>
/// <param name="logins">The login repository.</param>
/// <param name="accounts">The core account service.</param>
public sealed class UserLoginService(IUserLoginRepository logins, UserAccountService accounts)
{
    /// <summary>Link a provider account to <paramref name="user"/>; fails when it already belongs to any account.</summary>
    /// <param name="user">The user.</param>
    /// <param name="loginProvider">The provider, as its authentication scheme names it.</param>
    /// <param name="providerKey">The user's stable key at the provider (its subject).</param>
    /// <param name="displayName">Optional provider label for account settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IdentityResult> AddLoginAsync(IdentityUser user, string loginProvider, string providerKey, string? displayName = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(loginProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);

        if (await logins.FindAsync(loginProvider, providerKey, cancellationToken) is not null)
            return IdentityUserExtensions.Failure(IdentityErrorCodeConstants.LoginAlreadyAssociated, $"This {loginProvider} account is already linked.", IdentityUserExtensions.Param("LoginProvider", loginProvider));

        await logins.AddAsync(
            new IdentityUserLogin { UserId = user.Id, LoginProvider = loginProvider, ProviderKey = providerKey, ProviderDisplayName = displayName },
            cancellationToken);
        return IdentityResult.Success;
    }

    /// <summary>Unlink a provider account from <paramref name="user"/> and rotate the stamp.</summary>
    /// <param name="user">The user.</param>
    /// <param name="loginProvider">The provider.</param>
    /// <param name="providerKey">The user's key at the provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IdentityResult> RemoveLoginAsync(IdentityUser user, string loginProvider, string providerKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        return await logins.RemoveAsync(user.Id, loginProvider, providerKey, cancellationToken)
            ? await accounts.RotateSecurityStampAsync(user, cancellationToken)
            : IdentityResult.Success;
    }

    /// <summary>The user linked to a provider account, or null.</summary>
    /// <param name="loginProvider">The provider.</param>
    /// <param name="providerKey">The user's key at the provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IdentityUser?> FindByLoginAsync(string loginProvider, string providerKey, CancellationToken cancellationToken = default)
        => await logins.FindAsync(loginProvider, providerKey, cancellationToken) is { } login
            ? await accounts.FindByIdAsync(login.UserId, cancellationToken)
            : null;

    /// <summary>The user's linked provider accounts.</summary>
    /// <param name="user">The user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<IReadOnlyList<IdentityUserLogin>> GetLoginsAsync(IdentityUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        return logins.GetAsync(user.Id, cancellationToken);
    }
}
