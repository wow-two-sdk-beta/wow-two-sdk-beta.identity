using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Phones;

/// <summary>Opts the phone slice into an identity registration.</summary>
public static class PhoneIdentityBuilderExtensions
{
    /// <summary>Register <see cref="UserPhoneService"/> over <see cref="IOtpService"/>, registering the OTP service when absent.</summary>
    /// <param name="builder">The identity builder.</param>
    public static IdentityBuilder AddPhoneNumbers(this IdentityBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOtpService();
        builder.Services.TryAddScoped<UserPhoneService>();
        return builder;
    }
}
