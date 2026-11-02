using WoW.Two.Sdk.Beta.Be.Identity.Core.PersonalData;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.ProfilePictures;

/// <summary>Removes a deleted account's profile picture.</summary>
/// <param name="pictures">The picture store.</param>
public sealed class ProfilePictureDeletionHandler(IProfilePictureStore pictures) : IAccountDeletionHandler
{
    /// <inheritdoc />
    public Task HandleAsync(IdentityUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        return pictures.DeleteAsync(user.Id, cancellationToken);
    }
}
