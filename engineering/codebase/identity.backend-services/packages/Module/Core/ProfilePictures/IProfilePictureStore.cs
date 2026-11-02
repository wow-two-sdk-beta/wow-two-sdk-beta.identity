namespace WoW.Two.Sdk.Beta.Be.Identity.Core.ProfilePictures;

/// <summary>
/// Defines the seam keeping each person's one profile picture, the processed square WebP. The default keeps it in the
/// identity database (<c>identity_profile_pictures</c>); a file storage adapter can replace it.
/// </summary>
public interface IProfilePictureStore
{
    /// <summary>Keeps <paramref name="content"/> as the person's picture, replacing any earlier one.</summary>
    /// <param name="userId">The person.</param>
    /// <param name="content">The processed WebP.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The picture's version, which changes with its content.</returns>
    Task<string> SaveAsync(Guid userId, byte[] content, CancellationToken cancellationToken = default);

    /// <summary>The person's picture, or null.</summary>
    /// <param name="userId">The person.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ProfilePicture?> FindAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The version of the person's picture, read without its content; null when they have none.</summary>
    /// <param name="userId">The person.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string?> FindVersionAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Removes the person's picture, if any.</summary>
    /// <param name="userId">The person.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default);
}
