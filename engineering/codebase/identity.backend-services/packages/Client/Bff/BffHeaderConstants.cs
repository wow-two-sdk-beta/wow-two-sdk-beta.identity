namespace WoW.Two.Sdk.Beta.Be.Identity.Bff;

/// <summary>Holds the request headers the BFF reads.</summary>
public static class BffHeaderConstants
{
    /// <summary>Holds the antiforgery header an unsafe API request echoes <c>csrfToken</c> of the session route in.</summary>
    public const string CsrfToken = "X-Csrf-Token";
}
