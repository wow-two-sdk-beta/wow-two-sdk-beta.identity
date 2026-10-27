using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.EmailSignIn;

/// <summary>Opts passwordless email sign-in into an identity registration.</summary>
public static class EmailSignInIdentityBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="UserEmailSignInService"/> over the OTP service (registered when absent). Options
    /// come from <paramref name="configure"/>, then the host section <c>Identity:EmailSignIn</c>. The account API maps
    /// <c>email-sign-in/send</c> and <c>email-sign-in</c> once this is registered; links use <c>UserAccounts:Emails:MagicLink</c>.
    /// </summary>
    /// <param name="builder">The identity builder.</param>
    /// <param name="configure">Lifetimes, code length and confirmation.</param>
    public static IdentityBuilder AddEmailSignIn(this IdentityBuilder builder, Action<EmailSignInOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddModuleOptions(
            EmailSignInOptions.SectionName,
            configure,
            options => options
                .Validate(o => o.LinkLifetime > TimeSpan.Zero && o.CodeLifetime > TimeSpan.Zero, "EmailSignInOptions lifetimes must be positive.")
                .Validate(o => o.CodeLength is >= 4 and <= 12, "EmailSignInOptions.CodeLength must be 4–12."));
        builder.Services.AddOtpService();
        builder.Services.TryAddScoped<UserEmailSignInService>();
        return builder;
    }
}
