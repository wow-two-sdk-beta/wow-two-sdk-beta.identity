namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

/// <summary>Names the rate-limit policy the account API's credential routes take.</summary>
public static class AccountRateLimitPolicyConstants
{
    /// <summary>
    /// The per-address policy of every route that checks a credential or sends a code or link: <c>sign-in</c>,
    /// <c>sign-in/two-factor</c>, <c>sign-up</c>, <c>resend-confirmation-email</c>, <c>forgot-password</c>,
    /// <c>reset-password</c>, the email, passkey and phone sign-in routes. By default
    /// <see cref="DefaultPermitLimit"/> requests per <see cref="DefaultWindow"/> and client address, sliding; a host
    /// tunes it by declaring a policy of this name under the be sdk's <c>RateLimits:Policies</c> section, which then
    /// replaces the default. It applies behind <c>UseRateLimiter</c> (which <c>UseApiDefaults</c> adds).
    /// </summary>
    public const string Credentials = "identity-credentials";

    /// <summary>The requests one client address may send to the credential routes per <see cref="DefaultWindow"/> by default.</summary>
    public const int DefaultPermitLimit = 30;

    /// <summary>The window <see cref="DefaultPermitLimit"/> counts within by default.</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(1);
}
