using WoW.Two.Sdk.Beta.Be.Identity.Core.PersonalData;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>Removes a deleted person's trusted contacts, and their place as anyone's contact, with the account.</summary>
/// <param name="repository">The contacts.</param>
public sealed class TrustedContactDeletionHandler(ITrustedContactRepository repository) : IAccountDeletionHandler
{
    /// <inheritdoc />
    public Task HandleAsync(IdentityUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        return repository.RemoveAllOfAsync(user.Id, cancellationToken);
    }
}
