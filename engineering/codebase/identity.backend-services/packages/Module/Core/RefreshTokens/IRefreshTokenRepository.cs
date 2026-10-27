namespace WoW.Two.Sdk.Beta.Be.Identity.Core.RefreshTokens;

/// <summary>Defines refresh-token persistence with atomic consumption and family revocation.</summary>
public interface IRefreshTokenRepository
{
    /// <summary>Store a newly issued token.</summary>
    /// <param name="token">The token row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAsync(IdentityRefreshToken token, CancellationToken cancellationToken = default);

    /// <summary>The token row, or null.</summary>
    /// <param name="id">The token id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IdentityRefreshToken?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Mark the token consumed only while it is unconsumed and unrevoked; false when another caller won.</summary>
    /// <param name="id">The token id.</param>
    /// <param name="at">The consumption instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> TryConsumeAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>Revoke every live token of a family.</summary>
    /// <param name="familyId">The family id.</param>
    /// <param name="at">The revocation instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RevokeFamilyAsync(Guid familyId, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>Revoke every live token of a user.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="at">The revocation instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RevokeUserAsync(Guid userId, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>Delete tokens whose expiry is at or before <paramref name="before"/>; returns how many were deleted.</summary>
    /// <param name="before">The cut-off instant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> DeleteExpiredAsync(DateTimeOffset before, CancellationToken cancellationToken = default);
}
