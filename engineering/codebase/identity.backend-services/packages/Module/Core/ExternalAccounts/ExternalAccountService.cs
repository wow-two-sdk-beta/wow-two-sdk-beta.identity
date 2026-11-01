namespace WoW.Two.Sdk.Beta.Be.Identity.Core.ExternalAccounts;

/// <summary>
/// Provides external accounts: link a provider account (Google, Microsoft, Telegram, …) to a user, find the user behind
/// one, and unlink it. Unlinking rotates the security stamp. The OAuth handshake itself stays with the OAuth packages.
/// </summary>
/// <param name="externalAccounts">The external account repository.</param>
/// <param name="accounts">The core account service.</param>
public sealed class ExternalAccountService(IExternalAccountRepository externalAccounts, UserAccountService accounts)
{
    /// <summary>Link a provider account to <paramref name="user"/>; fails when it already belongs to any account.</summary>
    /// <param name="user">The user.</param>
    /// <param name="provider">The provider, as its authentication scheme names it.</param>
    /// <param name="providerKey">The user's stable key at the provider (its subject).</param>
    /// <param name="displayName">Optional provider label for account settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IdentityResult> LinkAsync(IdentityUser user, string provider, string providerKey, string? displayName = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);

        if (await externalAccounts.FindAsync(provider, providerKey, cancellationToken) is not null)
            return IdentityUserExtensions.Failure(IdentityErrorCodeConstants.ExternalAccountAlreadyLinked, $"This {provider} account is already linked.", IdentityUserExtensions.Param("Provider", provider));

        await externalAccounts.AddAsync(
            new IdentityExternalAccount { UserId = user.Id, Provider = provider, ProviderKey = providerKey, ProviderDisplayName = displayName },
            cancellationToken);
        return IdentityResult.Success;
    }

    /// <summary>Unlink a provider account from <paramref name="user"/> and rotate the stamp.</summary>
    /// <param name="user">The user.</param>
    /// <param name="provider">The provider.</param>
    /// <param name="providerKey">The user's key at the provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IdentityResult> UnlinkAsync(IdentityUser user, string provider, string providerKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        return await externalAccounts.RemoveAsync(user.Id, provider, providerKey, cancellationToken)
            ? await accounts.RotateSecurityStampAsync(user, cancellationToken)
            : IdentityResult.Success;
    }

    /// <summary>The user linked to a provider account, or null.</summary>
    /// <param name="provider">The provider.</param>
    /// <param name="providerKey">The user's key at the provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IdentityUser?> FindByExternalAccountAsync(string provider, string providerKey, CancellationToken cancellationToken = default)
        => await externalAccounts.FindAsync(provider, providerKey, cancellationToken) is { } account
            ? await accounts.FindByIdAsync(account.UserId, cancellationToken)
            : null;

    /// <summary>The user's linked provider accounts.</summary>
    /// <param name="user">The user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<IReadOnlyList<IdentityExternalAccount>> ListAsync(IdentityUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        return externalAccounts.GetAsync(user.Id, cancellationToken);
    }
}
