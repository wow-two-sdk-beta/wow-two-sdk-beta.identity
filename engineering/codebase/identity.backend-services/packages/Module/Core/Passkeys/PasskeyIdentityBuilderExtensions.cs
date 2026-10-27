using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Passkeys;

/// <summary>Opts the passkey slice into an identity registration.</summary>
public static class PasskeyIdentityBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="UserPasskeyService"/> and the passkey
    /// repository over <c>identity_passkeys</c>. Options come from <paramref name="configure"/>, then the host section
    /// <c>Identity:Passkeys</c>; the relying-party domain and at least one origin are required.
    /// </summary>
    /// <param name="builder">The identity builder.</param>
    /// <param name="configure">Relying party, origins and ceremony settings.</param>
    public static IdentityBuilder AddPasskeys(this IdentityBuilder builder, Action<PasskeyOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddModuleOptions(
            PasskeyOptions.SectionName,
            configure,
            options => options
                .Validate(o => !string.IsNullOrWhiteSpace(o.ServerDomain), "PasskeyOptions.ServerDomain is required.")
                .Validate(o => o.Origins.Count > 0, "PasskeyOptions.Origins needs at least one origin.")
                .Validate(o => o.CeremonyLifetime > TimeSpan.Zero, "PasskeyOptions.CeremonyLifetime must be positive."));
        builder.Services.AddDataProtection();
        builder.Services.TryAddScoped<IUserPasskeyRepository, EfUserPasskeyRepository>();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddScoped<UserPasskeyService>();
        return builder;
    }
}
