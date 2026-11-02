using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Invitations;

/// <summary>Opts invitations into an identity registration.</summary>
public static class InvitationIdentityBuilderExtensions
{
    /// <summary>
    /// Register <see cref="InvitationService"/> and its repository over <c>identity_invitations</c>; it needs the
    /// organizations slice. The account API then maps the invitation preview and acceptance, sign-up and phone sign-in take
    /// an <c>invitationToken</c>, and <c>MapOrganizationEndpoints</c> maps the managers' invitation routes.
    /// </summary>
    /// <param name="builder">The identity builder.</param>
    /// <param name="configure">Optional lifetime and link; the host section <c>Identity:Invitations</c> is applied last.</param>
    public static IdentityBuilder AddInvitations(this IdentityBuilder builder, Action<InvitationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        services.AddModuleOptions(
            InvitationOptions.SectionName,
            configure,
            options => options
                .Validate(o => o.Lifetime > TimeSpan.Zero, "Identity:Invitations:Lifetime must be positive.")
                .Validate(o => o.LinkTemplate is null || o.LinkTemplate.Contains("{token}", StringComparison.Ordinal), "Identity:Invitations:LinkTemplate must hold {token}."));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IInvitationRepository, EfInvitationRepository>();
        services.TryAddScoped<InvitationService>();
        return builder;
    }
}
