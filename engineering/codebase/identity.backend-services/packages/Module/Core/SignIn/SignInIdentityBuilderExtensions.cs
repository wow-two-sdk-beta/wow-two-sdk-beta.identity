using Microsoft.Extensions.DependencyInjection.Extensions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignIn;

/// <summary>Opts sign-in orchestration into an identity registration.</summary>
public static class SignInIdentityBuilderExtensions
{
    /// <summary>
    /// Register <see cref="SignInService"/>. It composes whichever of the password, lockout, token and
    /// two-factor slices are registered; preconditions come from <see cref="IdentityCoreOptions.SignIn"/>.
    /// </summary>
    /// <param name="builder">The identity builder.</param>
    public static IdentityBuilder AddSignIn(this IdentityBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddScoped<SignInService>();
        return builder;
    }
}
