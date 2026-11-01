namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

/// <summary>Holds the error codes only the account HTTP API raises, beside <c>IdentityErrorCodeConstants</c>.</summary>
public static class AccountApiErrorCodeConstants
{
    /// <summary>The host cannot issue bearer tokens or refresh them; sign in with <c>?useCookies=true</c>.</summary>
    public const string BearerSignInUnsupported = "BearerSignInUnsupported";
}
