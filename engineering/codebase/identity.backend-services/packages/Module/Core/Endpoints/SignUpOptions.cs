namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

/// <summary>Holds who may create an account through the account API.</summary>
/// <remarks>
/// Set in code with <c>.AddAccountEndpoints(o => …)</c> or in the host section <c>Identity:SignUp</c>, which is applied
/// last and read live. In <see cref="SignUpMode.Invite"/> mode <c>sign-up</c> and a first phone sign-in answer <c>403</c>
/// (message key <c>SignUpClosed</c>) unless they carry a pending invitation's token; an admin's code path
/// (<c>UserAccountService</c>) creates every other account; sign-in and every other route stay open.
/// </remarks>
public sealed record SignUpOptions
{
    /// <summary>The host configuration section.</summary>
    public const string SectionName = "Identity:SignUp";

    /// <summary>Gets or sets who may create an account. Default <see cref="SignUpMode.Invite"/>.</summary>
    public SignUpMode Mode { get; set; } = SignUpMode.Invite;

    /// <summary>Gets whether anyone may create an account without an invitation.</summary>
    public bool IsOpen => Mode == SignUpMode.Open;
}
