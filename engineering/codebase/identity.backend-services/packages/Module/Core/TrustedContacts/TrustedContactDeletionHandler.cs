using WoW.Two.Sdk.Beta.Be.Identity.Core.PersonalData;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>Removes a deleted person's trusted contacts, their place as anyone's contact, and their known browsers, with the account.</summary>
/// <param name="repository">The contacts.</param>
/// <param name="devices">The known browsers and waiting requests.</param>
public sealed class TrustedContactDeletionHandler(ITrustedContactRepository repository, IDeviceConfirmationRepository devices) : IAccountDeletionHandler
{
    /// <inheritdoc />
    public async Task HandleAsync(IdentityUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        await repository.RemoveAllOfAsync(user.Id, cancellationToken);
        await devices.RemoveAllOfAsync(user.Id, cancellationToken);
    }
}
