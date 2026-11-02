using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Phones;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Core.ProfilePictures;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInDeclaration;

/// <summary>
/// Provides the sign-in declaration from the live settings, as the routes it describes read them: the password method
/// while the password and sign-in slices are registered and <c>Identity:Methods:Password</c> is on, email while
/// <c>Identity:Methods:Email</c> is on, the phone code while the Telegram bot is on
/// (<c>Identity:Telegram:IsEnabled</c>), sign-up from <c>Identity:SignUp:Mode</c> and its profile fields from
/// <c>Identity:SignUp:Profile</c> (a picture only while profile pictures are registered), the verifications from
/// <c>Identity:SignIn</c>, and the instance's one look from <c>Identity:Pages</c>.
/// </summary>
/// <param name="services">The request's services, for the slices a registration may leave out.</param>
/// <param name="pages">The instance's look; <c>Identity:Pages</c> reloads live.</param>
/// <param name="identity">The password rules and sign-in preconditions.</param>
public sealed class SignInDeclarationService(
    IServiceProvider services,
    IOptionsMonitor<HostedPageOptions> pages,
    IdentityCoreOptions identity)
{
    /// <summary>
    /// Builds the declaration for the app <paramref name="clientId"/> names, with its messages in <paramref name="locale"/>.
    /// Every app gets the instance's one look, so the answer never tells which apps exist; the client will narrow the methods
    /// later.
    /// </summary>
    /// <param name="clientId">The OIDC client the sign-in serves, or null for the instance's own pages; it does not change the look.</param>
    /// <param name="locale">The locale of the messages; one the pages do not offer takes <c>en</c>.</param>
    public SignInOptionsDto Build(string? clientId, string? locale)
    {
        _ = clientId; // one look per instance: a client will narrow the methods, never the look
        var instance = pages.CurrentValue;
        var signIn = identity.SignIn;
        var methods = services.GetService<IOptionsMonitor<SignInMethodOptions>>()?.CurrentValue ?? new SignInMethodOptions();
        var signUp = services.GetService<IOptionsMonitor<SignUpOptions>>()?.CurrentValue;
        var input = new SignInDeclarationInput
        {
            BrandName = instance.BrandName,
            Theme = instance.Theme,
            BackgroundUrl = Filled(instance.BackgroundUrl),
            SignUpMode = signUp?.Mode ?? SignUpMode.Invite,
            SignUpProfile = signUp?.Profile ?? new SignUpProfileOptions(),
            PictureRules = services.GetService<IOptionsMonitor<ProfilePictureOptions>>()?.CurrentValue,
            IsPersistenceOffered = services.GetService<Sessions.SessionOptions>()?.IsPersistenceOffered == true,
            Password = methods.Password.IsEnabled && IsPasswordSignInOn() ? identity.Password : null,
            IsEmailEnabled = methods.Email.IsEnabled,
            IsEmailIdentifierAllowed = signIn.IsEmailIdentifierAllowed && methods.Email.IsEnabled,
            PhoneCountries = PhoneCountries(),
            PhoneCodeLength = services.GetService<OtpOptions>()?.CodeLength ?? new OtpOptions().CodeLength,
            IsConfirmedEmailRequired = signIn.IsConfirmedEmailRequired,
            IsConfirmedPhoneNumberRequired = signIn.IsConfirmedPhoneNumberRequired,
        };

        return SignInDeclarationBuilder.Build(input, SignInLocaleResolver.Resolve(locale, []));
    }

    /// <summary>Whether <c>sign-in</c> signs in with a password: the sign-in slice and the password slice behind it are registered.</summary>
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
