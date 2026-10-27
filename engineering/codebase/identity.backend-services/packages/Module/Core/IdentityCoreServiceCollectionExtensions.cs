using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;
using WoW.Two.Sdk.Beta.Be.Web.ErrorTranslation;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>DI entry point for identity core — the mandatory slice every app starts from.</summary>
public static class IdentityCoreServiceCollectionExtensions
{
    /// <summary>
    /// Register identity core: options, the <see cref="UserAccountService"/> facade, the principal factory, the ru and uz
    /// texts of <see cref="IdentityErrorCodeConstants"/> for error translation, and the Module's own database —
    /// <see cref="IdentityDbContext"/> with its user store, and the embedded SQL as the be sdk migration set
    /// <see cref="IdentityDatabaseOptions.MigrationSetName"/>. Chain further slices on the returned builder.
    /// </summary>
    /// <remarks>
    ///   - the database comes from <paramref name="configureDatabase"/>, then the host section <c>Identity:Database</c>
    ///   - the host applies the set with the be sdk's boot or deploy helpers (<c>MigrateBespokeOnStartupAsync</c>,
    ///     <c>MigrateBespokeForDeployAsync</c>) beside its own sets
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional identity options.</param>
    /// <param name="configureDatabase">Optional database options: engine, schema, connection and the schema owner kind.</param>
    public static IdentityBuilder AddUserAccounts(
        this IServiceCollection services,
        Action<IdentityCoreOptions>? configure = null,
        Action<IdentityDatabaseOptions>? configureDatabase = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddValidatedOptions<IdentityCoreOptions>(
            configure,
            builder => builder
                .Validate(options => options.Password.MinLength > 0, "IdentityCoreOptions.Password.MinLength must be positive.")
                .Validate(
                    options => options.Password.MaxLength >= options.Password.MinLength,
                    "IdentityCoreOptions.Password.MaxLength must not be below MinLength.")
                .Validate(
                    options => options.Password.RequiredUniqueChars >= 0 && options.Password.RequiredUniqueChars <= options.Password.MaxLength,
                    "IdentityCoreOptions.Password.RequiredUniqueChars must lie between 0 and MaxLength.")
                .Validate(options => options.Lockout.MaxFailedAttempts > 0, "IdentityCoreOptions.Lockout.MaxFailedAttempts must be positive.")
                .Validate(options => options.Lockout.DefaultLockout > TimeSpan.Zero, "IdentityCoreOptions.Lockout.DefaultLockout must be positive."));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<UserAccountService>();
        services.TryAddScoped<UserClaimsPrincipalFactory>();
        services.AddIdentityDatabase(configureDatabase);

        // The texts left core with Identity; the contract carries them and the module contributes them, once per culture.
        foreach (var (culture, messages) in IdentityErrorTextConstants.Messages)
            services.AddErrorTexts(culture, messages);

        return new IdentityBuilder(services);
    }
}
