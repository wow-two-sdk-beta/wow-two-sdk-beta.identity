using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Channels;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Emails;

/// <summary>Opts the email slice into an identity registration.</summary>
public static class EmailIdentityBuilderExtensions
{
    /// <summary>
    /// Register <see cref="UserEmailService"/> and offer a confirmed email as a verified channel
    /// (<see cref="EmailVerifiedChannelSource"/>); needs <c>.AddUserTokens(...)</c> for its tokens.
    /// </summary>
    /// <param name="builder">The identity builder.</param>
    public static IdentityBuilder AddEmailConfirmation(this IdentityBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddScoped<UserEmailService>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IUserVerifiedChannelSource, EmailVerifiedChannelSource>());
        return builder;
    }
}
