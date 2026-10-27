using Microsoft.Extensions.DependencyInjection.Extensions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Emails;

/// <summary>Opts the email slice into an identity registration.</summary>
public static class EmailIdentityBuilderExtensions
{
    /// <summary>Register <see cref="UserEmailService"/>; needs <c>.AddUserTokens(...)</c> for its tokens.</summary>
    /// <param name="builder">The identity builder.</param>
    public static IdentityBuilder AddEmailConfirmation(this IdentityBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddScoped<UserEmailService>();
        return builder;
    }
}
