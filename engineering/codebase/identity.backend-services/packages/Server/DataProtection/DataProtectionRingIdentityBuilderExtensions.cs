using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Core;

namespace WoW.Two.Sdk.Beta.Be.Identity.DataProtection;

/// <summary>Keeps an instance's Data Protection ring in its identity database.</summary>
public static class DataProtectionRingIdentityBuilderExtensions
{
    /// <summary>
    /// Register ASP.NET Core Data Protection over the Module's store: the ring in <c>identity_data_protection_keys</c>
    /// through <see cref="IdentityDbContext"/>, shared by every replica, each key wrapped by the active certificate of
    /// <see cref="DataProtectionRingOptions.Certificates"/>. The sign-in cookie, antiforgery tokens and passkey ceremony
    /// state are sealed by this ring.
    /// </summary>
    /// <remarks>
    ///   - a boot fails closed on unreadable or unfit certificates, and on an empty list outside a local run; a local run
    ///     may list none and stores the ring unwrapped, with a warning
    ///   - settings are read late, so a host's configuration (a test host's included) reaches them
    ///   - replaces a file-system ring (<c>AddPersistentDataProtection</c>); register only one
    /// </remarks>
    /// <param name="builder">The identity builder from <c>AddUserAccounts</c>.</param>
    /// <param name="configure">Optional ring options; the host section <c>Identity:DataProtection</c> is applied last.</param>
    public static IdentityBuilder AddDataProtectionRing(this IdentityBuilder builder, Action<DataProtectionRingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;

        services.AddModuleOptions(DataProtectionRingOptions.SectionName, configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<DataProtectionRingOptions>, DataProtectionRingOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(provider => new DataProtectionRingCertificates(
            provider.GetRequiredService<DataProtectionRingOptions>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetService<IHostEnvironment>()));

        services.AddDataProtection();
        services.AddOptions<DataProtectionOptions>()
            .Configure<DataProtectionRingOptions>(static (protection, ring) => protection.ApplicationDiscriminator = ring.ApplicationName);
        services.AddSingleton<IConfigureOptions<KeyManagementOptions>, DataProtectionRingConfiguration>();
        return builder;
    }
}
