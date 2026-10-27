using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PersonalData;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

/// <summary>Opts the organizations slice into an identity registration.</summary>
public static class OrganizationIdentityBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="OrganizationService"/> and the
    /// repository over <c>identity_organizations</c> and <c>identity_memberships</c>. Roles and their permissions come
    /// from <paramref name="configure"/>, then the host section <c>Identity:Organizations</c>. Principals then carry
    /// organization role and permission claims.
    /// </summary>
    /// <param name="builder">The identity builder.</param>
    /// <param name="configure">The owner role and the other roles with their permissions.</param>
    public static IdentityBuilder AddOrganizations(this IdentityBuilder builder, Action<OrganizationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddModuleOptions(
            OrganizationOptions.SectionName,
            configure,
            options => options
                .Validate(o => !string.IsNullOrWhiteSpace(o.OwnerRole), "OrganizationOptions.OwnerRole is required.")
                .Validate(o => o.Roles.Keys.All(name => !string.IsNullOrWhiteSpace(name)), "OrganizationOptions.Roles needs non-empty role names."));
        builder.Services.TryAddScoped<IOrganizationRepository, EfOrganizationRepository>();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddScoped<OrganizationService>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IPersonalDataExporter, OrganizationPersonalDataExporter>());
        return builder;
    }
}
