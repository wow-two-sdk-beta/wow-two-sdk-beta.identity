using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Phones;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInDeclaration;

/// <summary>
/// Provides the sign-in declaration from the live settings, as the routes it describes read them: the password method
/// while the password and sign-in slices are registered, the phone code while the Telegram bot is on
/// (<c>Identity:Telegram:IsEnabled</c>), sign-up from <c>Identity:SignUp:IsEnabled</c>, the verifications from
/// <c>Identity:SignIn</c>, and the look from <c>Identity:Pages</c> with a registered app's own theme and background over it.
/// </summary>
/// <param name="services">The request's services, for the slices a registration may leave out.</param>
/// <param name="pages">The instance's look; <c>Identity:Pages</c> reloads live.</param>
/// <param name="identity">The password rules and sign-in preconditions.</param>
/// <param name="looks">The registered apps' looks; null when no client registry is hosted.</param>
public sealed class SignInDeclarationService(
    IServiceProvider services,
    IOptionsMonitor<HostedPageOptions> pages,
    IdentityCoreOptions identity,
    IHostedPageLookSource? looks = null)
{
    /// <summary>
    /// Builds the declaration for the app <paramref name="clientId"/> names, with its messages in <paramref name="locale"/>.
    /// A client that is not registered, or names no look, gets the instance's look, so the answer never tells which apps exist.
    /// </summary>
    /// <param name="clientId">The OIDC client the sign-in serves, or null for the instance's own pages.</param>
    /// <param name="locale">The locale of the messages; one the pages do not offer takes <c>en</c>.</param>
    public SignInOptionsDto Build(string? clientId, string? locale)
    {
        var instance = pages.CurrentValue;
        var look = string.IsNullOrWhiteSpace(clientId) ? null : looks?.Find(clientId.Trim());
        var signIn = identity.SignIn;
        var input = new SignInDeclarationInput
        {
            BrandName = instance.BrandName,
            Theme = Filled(look?.Theme) ?? instance.Theme,
            BackgroundUrl = Filled(look?.BackgroundUrl) ?? Filled(instance.BackgroundUrl),
            IsSignUpEnabled = services.GetService<IOptionsMonitor<SignUpOptions>>()?.CurrentValue.IsEnabled == true,
            Password = IsPasswordSignInOn() ? identity.Password : null,
            IsEmailLoginAllowed = signIn.IsEmailLoginAllowed,
            PhoneCountries = PhoneCountries(),
            PhoneCodeLength = services.GetService<OtpOptions>()?.CodeLength ?? new OtpOptions().CodeLength,
            IsConfirmedEmailRequired = signIn.IsConfirmedEmailRequired,
            IsConfirmedPhoneNumberRequired = signIn.IsConfirmedPhoneNumberRequired,
        };

        return SignInDeclarationBuilder.Build(input, SignInLocaleResolver.Resolve(locale, []));
    }

    /// <summary>Whether <c>login</c> signs in with a password: the sign-in slice and the password slice behind it are registered.</summary>
    private bool IsPasswordSignInOn()
        => services.GetService<IServiceProviderIsService>() is { } registered
            && registered.IsService(typeof(SignInService))
            && registered.IsService(typeof(UserPasswordService));

    /// <summary>The allowed countries while the phone routes answer, as <c>phone-sign-in/send</c> checks it; null while they are off.</summary>
    private IReadOnlyList<string>? PhoneCountries()
    {
        if (services.GetService<UserPhoneSignInService>() is not { IsEnabled: true })
            return null;

        return services.GetService<IOptionsMonitor<PhoneOptions>>()?.CurrentValue.AllowedCountries ?? [];
    }

    private static string? Filled(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
