using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;
using WoW.Two.Sdk.Beta.Be.Identity.PasswordHashing.Argon2;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;

/// <summary>Opts the password slice and its optional breach check into an identity registration.</summary>
public static class PasswordIdentityBuilderExtensions
{
    /// <summary>
    /// Register <see cref="UserPasswordService"/> with the <see cref="PasswordOptions"/> rules and an Argon2id
    /// hasher. A hasher the host registered earlier wins.
    /// </summary>
    /// <param name="builder">The identity builder.</param>
    public static IdentityBuilder AddArgon2Passwords(this IdentityBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton<IPasswordHasher<IdentityUser>, Argon2PasswordHasher<IdentityUser>>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IUserPasswordValidator, PasswordRulesValidator>());
        builder.Services.TryAddScoped<UserPasswordService>();
        return builder;
    }

    /// <summary>Reject passwords found in the Pwned Passwords corpus, queried by k-anonymity range.</summary>
    /// <param name="builder">The identity builder.</param>
    /// <param name="configure">Optional endpoint, threshold, timeout and fail-open settings.</param>
    public static IdentityBuilder AddBreachedPasswordCheck(
        this IdentityBuilder builder,
        Action<BreachedPasswordOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddValidatedOptions(
            configure,
            options => options
                .Validate(o => o.BaseAddress.IsAbsoluteUri, "BreachedPasswordOptions.BaseAddress must be absolute.")
                .Validate(o => o.MinimumBreachCount > 0, "BreachedPasswordOptions.MinimumBreachCount must be positive.")
                .Validate(o => o.Timeout > TimeSpan.Zero, "BreachedPasswordOptions.Timeout must be positive."));
        builder.Services.AddHttpClient<IPwnedPasswordsClient, PwnedPasswordsClient>((serviceProvider, http) =>
        {
            var options = serviceProvider.GetRequiredService<BreachedPasswordOptions>();
            http.BaseAddress = options.BaseAddress;
            http.Timeout = options.Timeout;
            http.DefaultRequestHeaders.UserAgent.ParseAdd("WoW2.Sdk.Beta.Be");
        });
        builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<IUserPasswordValidator, BreachedPasswordValidator>());
        return builder;
    }
}
