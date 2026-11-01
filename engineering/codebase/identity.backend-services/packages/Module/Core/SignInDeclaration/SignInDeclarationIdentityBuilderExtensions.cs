using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInDeclaration;

/// <summary>Opts the public sign-in declaration into an identity registration.</summary>
public static class SignInDeclarationIdentityBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="SignInDeclarationService"/> over <see cref="HostedPageOptions"/>; map its route with
    /// <c>MapSignInOptionsEndpoint</c>. A host serving an OIDC client registry adds its <see cref="IHostedPageLookSource"/>
    /// so each app's own theme and background apply.
    /// </summary>
    /// <remarks>A blank brand name, a theme that is no registry id, or a background that is neither a same-origin path nor an HTTPS URL stops the start.</remarks>
    /// <param name="builder">The identity builder.</param>
    /// <param name="pages">The instance's look, from code; the host section <c>Identity:Pages</c> is applied last.</param>
    public static IdentityBuilder AddSignInDeclaration(this IdentityBuilder builder, Action<HostedPageOptions>? pages = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddModuleOptions(
            HostedPageOptions.SectionName,
            pages,
            options => options
                .Validate(o => !string.IsNullOrWhiteSpace(o.BrandName), "Identity:Pages:BrandName must not be empty.")
                .Validate(
                    o => HostedPageLookRules.IsThemeName(o.Theme),
                    "Identity:Pages:Theme must be an fe sdk theme id: lower-case letters and digits in hyphen-joined words, such as platform.")
                .Validate(
                    o => string.IsNullOrEmpty(o.BackgroundUrl) || HostedPageLookRules.IsBackgroundUrl(o.BackgroundUrl),
                    "Identity:Pages:BackgroundUrl must be a path on the instance's origin, such as /backgrounds/app.jpg, or an absolute HTTPS URL."));
        builder.Services.TryAddScoped<SignInDeclarationService>();
        return builder;
    }
}
