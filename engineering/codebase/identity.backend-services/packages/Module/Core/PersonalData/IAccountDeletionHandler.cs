namespace WoW.Two.Sdk.Beta.Be.Identity.Core.PersonalData;

/// <summary>
/// Defines the product's part of deleting an account: erase or anonymize what it stores for the user. Every registered
/// handler runs before the identity rows go; a handler must be safe to run again after a failed deletion.
/// </summary>
public interface IAccountDeletionHandler
{
    /// <summary>Erases or anonymizes the user's data in the product's stores.</summary>
    /// <param name="user">The user being deleted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task HandleAsync(IdentityUser user, CancellationToken cancellationToken = default);
}
