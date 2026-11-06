namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>Stores the browsers an account knows and the sign-ins waiting for a contact's press.</summary>
public interface IDeviceConfirmationRepository
{
    /// <summary>Whether the account knows the browser.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="deviceHash">The browser's hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> IsKnownAsync(Guid userId, string deviceHash, CancellationToken cancellationToken = default);

    /// <summary>Records the browser for the account; one already known keeps its first row.</summary>
    /// <param name="device">The row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RememberAsync(IdentityKnownDevice device, CancellationToken cancellationToken = default);

    /// <summary>Adds a request, deleting the account's open ones and every request past its end.</summary>
    /// <param name="confirmation">The row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ReplaceAsync(IdentityDeviceConfirmation confirmation, CancellationToken cancellationToken = default);

    /// <summary>The request a contact's button names.</summary>
    /// <param name="id">The request's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IdentityDeviceConfirmation?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The request a poll token names.</summary>
    /// <param name="tokenHash">The token's hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IdentityDeviceConfirmation?> FindByTokenAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Records a contact's answer on a request nobody answered; false when another answer came first.</summary>
    /// <param name="id">The request's id.</param>
    /// <param name="contactUserId">The contact.</param>
    /// <param name="isApproved">The answer.</param>
    /// <param name="decidedAt">When (UTC).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> TryDecideAsync(Guid id, Guid contactUserId, bool isApproved, DateTimeOffset decidedAt, CancellationToken cancellationToken = default);

    /// <summary>Deletes a request; false when it was gone already, so one poll alone spends it.</summary>
    /// <param name="id">The request's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> TryRemoveAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Deletes every request of the account, answered or not, when its own phone changes.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveRequestsOfAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Deletes the account's known browsers and requests, with the account.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveAllOfAsync(Guid userId, CancellationToken cancellationToken = default);
}
