namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

/// <summary>Names the account an email is sent to (confirmation resend, password reset).</summary>
public sealed record EmailAddressApiRequest
{
    /// <summary>The account email.</summary>
    public required string Email { get; init; }

    /// <summary>
    /// Where the hosted pages continue once the emailed link is used, such as the app's authorization request; kept only
    /// when it is a local path (<see cref="AccountReturnUrl.IsLocal"/>), and carried by the link as <c>returnUrl=</c>.
    /// </summary>
    public string? ReturnUrl { get; init; }
}
