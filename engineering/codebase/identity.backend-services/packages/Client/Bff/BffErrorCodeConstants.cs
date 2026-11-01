namespace WoW.Two.Sdk.Beta.Be.Identity.Bff;

/// <summary>Holds the <c>code</c> values of the problems the BFF answers.</summary>
public static class BffErrorCodeConstants
{
    /// <summary>Holds the 401 code: the API request has no live session.</summary>
    public const string SignInRequired = "SignInRequired";

    /// <summary>Holds the 403 code: the session lacks a permission the endpoint requires.</summary>
    public const string AccessDenied = "AccessDenied";

    /// <summary>Holds the 404 code: the route names an organization other than the session's.</summary>
    public const string NotFound = "NotFound";

    /// <summary>Holds the 400 code: an unsafe request of a session lacks a valid <see cref="BffHeaderConstants.CsrfToken"/>.</summary>
    public const string AntiforgeryValidationFailed = "AntiforgeryValidationFailed";
}
