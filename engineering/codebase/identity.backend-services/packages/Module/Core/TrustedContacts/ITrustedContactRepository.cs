namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>Defines persistence of accounts' trusted contacts, and the reads the slice's rules need beside them.</summary>
public interface ITrustedContactRepository
{
    /// <summary>The account's contacts, oldest first.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<IdentityTrustedContact>> ListContactsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The contacts of each of <paramref name="userIds"/>, oldest first.</summary>
    /// <param name="userIds">The accounts.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<IdentityTrustedContact>> ListContactsOfAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);

    /// <summary>The rows naming <paramref name="contactUserId"/> as a contact: the accounts trusting them, oldest first.</summary>
    /// <param name="contactUserId">The contact.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<IdentityTrustedContact>> ListTrustingAsync(Guid contactUserId, CancellationToken cancellationToken = default);

    /// <summary>The row of the pair, or null.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="contactUserId">The contact.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IdentityTrustedContact?> FindAsync(Guid userId, Guid contactUserId, CancellationToken cancellationToken = default);

    /// <summary>Saves a new row, within the caller's transaction when one is open.</summary>
    /// <param name="contact">The row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAsync(IdentityTrustedContact contact, CancellationToken cancellationToken = default);

    /// <summary>Deletes the pair's row.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="contactUserId">The contact.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether this call deleted it.</returns>
    Task<bool> TryRemoveAsync(Guid userId, Guid contactUserId, CancellationToken cancellationToken = default);

    /// <summary>Deletes every row naming <paramref name="userId"/>, as the account or as the contact.</summary>
    /// <param name="userId">The person.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveAllOfAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Whether the person holds an instance role (<c>admin</c>, <c>support</c>, <c>auditor</c>).</summary>
    /// <param name="userId">The person.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> HoldsInstanceRoleAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the person's session <paramref name="sessionId"/> was started by a successful sign-in with their own phone:
    /// the sign-in log row that started it is a <c>phone_code</c> or <c>phone_contact</c> sign-in through no contact.
    /// </summary>
    /// <param name="userId">The person.</param>
    /// <param name="sessionId">The session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> IsOwnPhoneSignInAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>The session, revoked or not, or null.</summary>
    /// <param name="sessionId">The session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IdentitySession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
