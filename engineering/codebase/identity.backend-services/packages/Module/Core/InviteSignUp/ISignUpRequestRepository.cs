namespace WoW.Two.Sdk.Beta.Be.Identity.Core.InviteSignUp;

/// <summary>Defines persistence of invitation sign-ups waiting for the bot; finishing one and signing its browser in are conditional, so each happens once.</summary>
public interface ISignUpRequestRepository
{
    /// <summary>Saves a new sign-up, pruning those a day past their expiry.</summary>
    /// <param name="request">The sign-up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAsync(IdentitySignUpRequest request, CancellationToken cancellationToken = default);

    /// <summary>The sign-up with the id, or null.</summary>
    /// <param name="id">The sign-up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IdentitySignUpRequest?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Finishes a waiting sign-up as joined by <paramref name="userId"/> or refused with <paramref name="failureCode"/>, dropping its picture.</summary>
    /// <param name="id">The sign-up.</param>
    /// <param name="userId">The person who joined; null when refused.</param>
    /// <param name="failureCode">Why it was refused; null when joined.</param>
    /// <param name="at">The instant it finished.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether this call finished it; false when it was finished already.</returns>
    Task<bool> TryCompleteAsync(Guid id, Guid? userId, string? failureCode, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>Marks a joined sign-up's browser signed in.</summary>
    /// <param name="id">The sign-up.</param>
    /// <param name="at">The instant of the sign-in.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether this call marked it; false once it was signed in.</returns>
    Task<bool> TryMarkSignedInAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken = default);
}
