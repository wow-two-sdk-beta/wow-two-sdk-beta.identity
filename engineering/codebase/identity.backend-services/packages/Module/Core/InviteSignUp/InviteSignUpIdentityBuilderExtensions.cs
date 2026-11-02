using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.InviteSignUp;

/// <summary>Opts sign-up from an invitation, with the phone the bot verifies, into an identity registration.</summary>
public static class InviteSignUpIdentityBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="InviteSignUpService"/>, its store over <c>identity_sign_up_requests</c> and the bot's handler for
    /// its start links; it needs invitations, phone sign-in (the bot) and profile pictures. Map its routes with
    /// <c>MapInviteSignUpEndpoints</c>. The profile fields it asks for come from <c>Identity:SignUp:Profile</c>.
    /// </summary>
    /// <param name="builder">The identity builder.</param>
    public static IdentityBuilder AddInviteSignUp(this IdentityBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddScoped<ISignUpRequestRepository, EfSignUpRequestRepository>();
        builder.Services.TryAddScoped<InviteSignUpService>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<ITelegramLinkCompletionHandler, InviteSignUpTelegramLinkHandler>());
        return builder;
    }
}
