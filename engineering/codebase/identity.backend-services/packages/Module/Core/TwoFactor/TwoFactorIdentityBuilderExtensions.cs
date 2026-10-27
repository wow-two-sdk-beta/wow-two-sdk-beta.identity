using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Tokens;
using WoW.Two.Sdk.Beta.Be.Identity.Mfa.Totp;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TwoFactor;

/// <summary>Opts the two-factor slice into an identity registration.</summary>
public static class TwoFactorIdentityBuilderExtensions
{
    /// <summary>
    /// Register <see cref="UserTwoFactorService"/> (authenticator + recovery codes) and
    /// <see cref="UserTwoFactorMethodService"/> (delivered codes) with the TOTP and OTP services and
    /// the stored-token repository over <see cref="IdentityDbContext"/>. Two-factor sign-in also needs <c>.AddUserTokens(...)</c>;
    /// each delivered method needs its channel registered (<c>AddSmsOtpDelivery</c>, <c>AddWhatsAppOtpDelivery</c>, …).
    /// Options come from <paramref name="configure"/>, then the host section <c>Identity:TwoFactor</c>.
    /// </summary>
    /// <param name="builder">The identity builder.</param>
    /// <param name="configure">Authenticator label, recovery-code count and delivered-code methods.</param>
    public static IdentityBuilder AddTwoFactor(this IdentityBuilder builder, Action<TwoFactorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddModuleOptions(
            TwoFactorOptions.SectionName,
            configure,
            options => options
                .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer), "TwoFactorOptions.Issuer is required.")
                .Validate(o => o.RecoveryCodeCount is > 0 and <= 100, "TwoFactorOptions.RecoveryCodeCount must lie between 1 and 100.")
                .Validate(o => o.Methods.Values.All(m => !string.IsNullOrWhiteSpace(m.Channel)), "Every TwoFactorOptions.Methods entry needs a Channel.")
                .Validate(o => o.Methods.Values.All(m => m.Code.IsValid), "Every TwoFactorOptions.Methods code needs a Length of 4–12, a known Kind and a positive Lifetime.")
                .Validate(o => !o.Methods.ContainsKey(TwoFactorMethodNameConstants.Authenticator), "'authenticator' is reserved; name delivered methods otherwise."));
        builder.Services.AddTotp();
        builder.Services.AddOtpService();
        builder.AddStoredUserTokens();
        builder.Services.TryAddScoped<UserTwoFactorService>();
        builder.Services.TryAddScoped<UserTwoFactorMethodService>();
        return builder;
    }

    /// <summary>Register the stored-token repository over <see cref="IdentityDbContext"/>.</summary>
    internal static void AddStoredUserTokens(this IdentityBuilder builder)
    {
        builder.Services.TryAddScoped<IUserTokenRepository, EfUserTokenRepository>();
    }
}
