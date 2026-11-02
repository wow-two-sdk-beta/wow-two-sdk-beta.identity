using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.ProfilePictures;

/// <summary>Keeps profile pictures in <c>identity_profile_pictures</c> through <see cref="IdentityDbContext"/>; the version is the content's SHA-256, first 16 hex digits.</summary>
/// <param name="context">The identity context.</param>
/// <param name="time">The clock a kept picture is stamped with.</param>
public sealed class EfProfilePictureStore(IdentityDbContext context, TimeProvider time) : IProfilePictureStore
{
    private DbSet<IdentityProfilePicture> Pictures => context.Set<IdentityProfilePicture>();

    /// <inheritdoc />
    public async Task<string> SaveAsync(Guid userId, byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var version = Convert.ToHexStringLower(SHA256.HashData(content))[..16];
        var now = time.GetUtcNow();
        var kept = await Pictures.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        if (kept is null)
        {
            kept = new IdentityProfilePicture { UserId = userId };
            Pictures.Add(kept);
        }

        kept.Content = content;
        kept.ContentType = ProfilePictureTypeConstants.Webp;
        kept.Version = version;
        kept.UpdatedAt = now;
        await context.SaveChangesAsync(cancellationToken);
        context.Entry(kept).State = EntityState.Detached;
        return version;
    }

    /// <inheritdoc />
    public async Task<ProfilePicture?> FindAsync(Guid userId, CancellationToken cancellationToken = default)
        => await Pictures.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new ProfilePicture { Content = p.Content, ContentType = p.ContentType, Version = p.Version, UpdatedAt = p.UpdatedAt })
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task<string?> FindVersionAsync(Guid userId, CancellationToken cancellationToken = default)
        => Pictures.AsNoTracking().Where(p => p.UserId == userId).Select(p => p.Version).FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default)
        => Pictures.Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);
}
