using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.OneTimeCodes;

/// <summary>Opts the database one-time-code store into an identity registration.</summary>
public static class OneTimeCodeIdentityBuilderExtensions
{
    /// <summary>
    /// Keeps one-time codes in <c>identity_otp_codes</c> (<see cref="EfOtpRepository"/>) in place of the in-memory store,
    /// whether <c>AddOtpService</c> ran before or runs after, so every instance on the schema verifies the same codes.
    /// Instances share them only with one <c>Identity:Otp:HashKey</c>.
    /// </summary>
    /// <param name="builder">The identity builder.</param>
    public static IdentityBuilder AddDatabaseOtpStore(this IdentityBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOtpService();
        builder.Services.Replace(ServiceDescriptor.Scoped<IOtpRepository, EfOtpRepository>());
        return builder;
    }
}
