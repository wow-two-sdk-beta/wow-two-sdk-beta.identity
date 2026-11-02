namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

/// <summary>Signs an account up with an email and password.</summary>
public sealed record SignUpApiRequest
{
    /// <summary>The account email.</summary>
    public required string Email { get; init; }

    /// <summary>The initial password.</summary>
    public required string Password { get; init; }

    /// <summary>Optional user name; the email when omitted.</summary>
    public string? UserName { get; init; }

    /// <summary>
    /// The token of an invitation link: the account joins its organization with its role, and signs up even while sign-up
    /// is by invitation only. An invitation naming an email takes only that email.
    /// </summary>
    public string? InvitationToken { get; init; }

    /// <summary>
    /// Where the hosted pages continue once the emailed link is used, such as the app's authorization request; kept only
    /// when it is a local path (<see cref="AccountReturnUrl.IsLocal"/>), and carried by the link as <c>returnUrl=</c>.
    /// </summary>
    public string? ReturnUrl { get; init; }
}
