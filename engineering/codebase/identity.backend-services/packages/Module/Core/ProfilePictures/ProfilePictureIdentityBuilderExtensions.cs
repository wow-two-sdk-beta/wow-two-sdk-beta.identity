using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PersonalData;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.ProfilePictures;

/// <summary>Opts profile pictures into an identity registration.</summary>
public static class ProfilePictureIdentityBuilderExtensions
{
    /// <summary>
    /// Registers the picture rules (<see cref="ProfilePictureOptions"/>), the SkiaSharp processor and the database store
    /// behind <see cref="IProfilePictureStore"/> (a registered store wins), and deletes a person's picture with the
    /// account. The account API then serves <c>pictures/{userId}</c>, and the OIDC server issues the <c>picture</c> claim.
    /// </summary>
    /// <remarks>An accepted type outside JPEG, PNG and WebP, or bounds out of order, stops the start.</remarks>
    /// <param name="builder">The identity builder.</param>
    /// <param name="configure">The rules, from code; the host section <c>Identity:ProfilePictures</c> is applied last.</param>
    public static IdentityBuilder AddProfilePictures(this IdentityBuilder builder, Action<ProfilePictureOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddModuleOptions(
            ProfilePictureOptions.SectionName,
            configure,
            options => options
                .Validate(
                    o => o.AcceptedTypes.Count > 0 && o.AcceptedTypes.All(type => ProfilePictureOptions.SupportedTypes.Contains(type, StringComparer.OrdinalIgnoreCase)),
                    "Identity:ProfilePictures:AcceptedTypes must name image/jpeg, image/png or image/webp.")
                .Validate(o => o.MaxUploadBytes > 0, "Identity:ProfilePictures:MaxUploadBytes must be positive.")
                .Validate(
                    o => o.MinDimension > 0 && o.MinDimension <= o.MaxDimension && o.OutputSize > 0 && o.OutputSize <= o.MaxDimension,
                    "Identity:ProfilePictures needs 0 < MinDimension <= MaxDimension and 0 < OutputSize <= MaxDimension.")
                .Validate(o => o.Quality is >= 1 and <= 100, "Identity:ProfilePictures:Quality must be 1–100."));
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IProfilePictureProcessor, SkiaProfilePictureProcessor>();
        builder.Services.TryAddScoped<IProfilePictureStore, EfProfilePictureStore>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IAccountDeletionHandler, ProfilePictureDeletionHandler>());
        return builder;
    }
}
