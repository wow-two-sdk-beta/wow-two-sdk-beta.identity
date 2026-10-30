using Microsoft.AspNetCore.Authentication.Cookies;

namespace WoW.Two.Sdk.Beta.Be.Identity.OAuth.Endpoints;

/// <summary>Holds the options of the OAuth sign-in endpoints that
/// <see cref="OAuthSignInEndpointRouteBuilderExtensions.MapOAuthSignInEndpoints"/> maps.</summary>
public sealed record OAuthSignInEndpointOptions
{
    /// <summary>The <c>detail</c> of the 503 problem <c>sign-in</c> answers while the provider scheme is not registered,
    /// such as the settings the host needs. Default: a generic sentence naming no setting.</summary>
    public string NotConfiguredDetail { get; set; } = "Sign-in is not configured.";

    /// <summary>The scheme <c>sign-out</c> signs out of: the session cookie. Default
    /// <see cref="CookieAuthenticationDefaults.AuthenticationScheme"/>.</summary>
    public string SignOutScheme { get; set; } = CookieAuthenticationDefaults.AuthenticationScheme;
}
