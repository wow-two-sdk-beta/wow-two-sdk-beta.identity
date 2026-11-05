namespace WoW.Two.Sdk.Beta.Be.Identity.Bff;

/// <summary>Holds the session routes, relative to <see cref="IdentityBffOptions.RoutePrefix"/>.</summary>
public static class BffPathConstants
{
    /// <summary>Holds the session route (<c>GET</c>) the SPA reads.</summary>
    public const string Session = "/session";

    /// <summary>Holds the sign-in route (<c>GET ?returnUrl=</c>): the redirect to the authorize endpoint of the instance.</summary>
    public const string SignIn = "/sign-in";

    /// <summary>Holds the sign-out route (<c>POST</c>): ends the local session, answers the end-session URL of the instance.</summary>
    public const string SignOut = "/sign-out";

    /// <summary>Holds the route the instance posts revocation signals to (<c>POST</c>), mapped only with <see cref="IdentityBffOptions.SignalUrl"/>; its signature is the check.</summary>
    public const string Signal = "/signal";

    /// <summary>Holds the code callback the instance redirects to; register <c>{origin}{prefix}/callback</c> there.</summary>
    public const string Callback = "/callback";

    /// <summary>Holds the OpenID Connect handler's signed-out callback; unused by the session routes.</summary>
    public const string SignedOutCallback = "/signed-out-callback";

    /// <summary>Holds the front-channel sign-out the instance may call (OpenID Connect Front-Channel Logout); it ends the session whose <c>sid</c> it names.</summary>
    public const string FrontChannelSignOut = "/front-channel-sign-out";
}
