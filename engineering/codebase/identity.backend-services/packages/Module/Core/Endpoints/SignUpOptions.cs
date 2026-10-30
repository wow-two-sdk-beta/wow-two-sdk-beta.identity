namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

/// <summary>Holds whether anyone may create an account through the account API's <c>register</c> route.</summary>
/// <remarks>
/// Set in code with <c>.AddAccountEndpoints(o => …)</c> or in the host section <c>Identity:SignUp</c>, which is applied
/// last and read live. Off, <c>register</c> answers <c>403</c> (message key <c>SignUpClosed</c>), and only an admin's
/// code path (<c>UserAccountService</c>) creates accounts; sign-in and every other route stay open.
/// </remarks>
public sealed record SignUpOptions
{
    /// <summary>The host configuration section.</summary>
    public const string SectionName = "Identity:SignUp";

    /// <summary>Gets or sets whether <c>POST register</c> creates accounts for anyone who asks. Default <c>false</c>.</summary>
    public bool Enabled { get; set; }
}
