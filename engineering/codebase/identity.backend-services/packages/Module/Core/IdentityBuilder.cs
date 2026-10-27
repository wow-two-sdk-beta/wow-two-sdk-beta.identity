using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PersonalData;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Roles;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>Fluent builder returned by <c>AddUserAccounts</c>; each <c>.AddX()</c> opts a slice into the registration.</summary>
public sealed class IdentityBuilder
{
    /// <summary>Create the builder over <paramref name="services"/>.</summary>
    /// <param name="services">The service collection being configured.</param>
    public IdentityBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services;
    }

    /// <summary>The underlying service collection, for further composition.</summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Register the roles slice: <see cref="RoleService"/>, <see cref="UserRoleService"/> and their repositories over
    /// <see cref="IdentityDbContext"/>. Principals then carry role claims.
    /// </summary>
    public IdentityBuilder AddRoles()
    {
        Services.TryAddScoped<IRoleRepository, EfRoleRepository>();
        Services.TryAddScoped<IUserRoleRepository, EfUserRoleRepository>();
        Services.TryAddScoped<RoleService>();
        Services.TryAddScoped<UserRoleService>();
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<IPersonalDataExporter, RolePersonalDataExporter>());
        return this;
    }
}
