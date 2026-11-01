using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Phones;

/// <summary>Opts the phone slice into an identity registration.</summary>
public static class PhoneIdentityBuilderExtensions
{
    /// <summary>
    /// Register <see cref="UserPhoneService"/> over <see cref="IOtpService"/>, registering the OTP service when absent,
    /// and <see cref="PhoneNumberPolicy"/> over <see cref="PhoneOptions"/>. Options come from <paramref name="configure"/>,
    /// then the host section <c>Identity:Phone</c>; an unknown country code stops the start.
    /// </summary>
    /// <param name="builder">The identity builder.</param>
    /// <param name="configure">The allowed countries.</param>
    public static IdentityBuilder AddPhoneNumbers(this IdentityBuilder builder, Action<PhoneOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddModuleOptions(
            PhoneOptions.SectionName,
            configure,
            options => options.Validate(
                o => o.AllowedCountries.All(country => PhoneCountryConstants.DialCodes.ContainsKey(country)),
                "Identity:Phone:AllowedCountries names a country code the phone list does not know; use ISO 3166-1 alpha-2 codes such as UZ."));
        builder.Services.AddOtpService();
        builder.Services.TryAddSingleton<PhoneNumberPolicy>();
        builder.Services.TryAddScoped<UserPhoneService>();
        return builder;
    }
}
