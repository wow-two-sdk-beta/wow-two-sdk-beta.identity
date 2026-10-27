namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>
/// Defines the mandatory core store slice: user persistence + lookup by id / normalized name / normalized email. Optional
/// capabilities (password, email confirmation, lockout, …) are separate store slices layered on the same entity.
/// </summary>
public interface IUserRepository
{
    /// <summary>Persist a new user.</summary>
    /// <param name="user">The user to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CreateAsync(IdentityUser user, CancellationToken cancellationToken = default);

    /// <summary>Persist changes to an existing user.</summary>
    /// <param name="user">The tracked user whose accepted properties changed, or a detached user when no instance with the same key is tracked.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>A replacement instance is rejected while the original user is tracked.</remarks>
    Task UpdateAsync(IdentityUser user, CancellationToken cancellationToken = default);

    /// <summary>Delete a user.</summary>
    /// <param name="user">The user to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(IdentityUser user, CancellationToken cancellationToken = default);

    /// <summary>Find a user by primary key, or null.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IdentityUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Find a user by normalized user name, or null.</summary>
    /// <param name="normalizedUserName">The normalized user name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IdentityUser?> FindByNormalizedUserNameAsync(string normalizedUserName, CancellationToken cancellationToken = default);

    /// <summary>Find a user by normalized email, or null.</summary>
    /// <param name="normalizedEmail">The normalized email.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IdentityUser?> FindByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);

    /// <summary>Find the user holding a confirmed phone number, or null.</summary>
    /// <param name="phoneNumber">The phone number, compared exactly (store E.164).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IdentityUser?> FindByConfirmedPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);
}
