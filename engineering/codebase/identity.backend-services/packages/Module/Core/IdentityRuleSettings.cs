namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>Holds the rule sections a host sets for <see cref="IdentityCoreOptions"/>: <c>Identity:Password</c>, <c>Identity:SignIn</c> and <c>Identity:Lockout</c>.</summary>
/// <remarks>
///   - <c>AddUserAccounts</c> registers it beside <see cref="IdentityCoreOptions"/>, which still receives the same sections last,
///     so <c>config check</c> follows those three sections' members and finds a misspelt or retired key under them
///   - it holds no other section: <see cref="IdentityCoreOptions.User"/> and <see cref="IdentityCoreOptions.Claims"/> stay set in code
/// </remarks>
internal sealed record IdentityRuleSettings
{
    /// <summary>The host section the three rule sections sit under.</summary>
    public const string SectionName = "Identity";

    /// <summary>Gets the password rules (<c>Identity:Password</c>, with <c>Change</c> beneath).</summary>
    public PasswordOptions Password { get; } = new();

    /// <summary>Gets the sign-in preconditions (<c>Identity:SignIn</c>).</summary>
    public SignInOptions SignIn { get; } = new();

    /// <summary>Gets the lockout rules (<c>Identity:Lockout</c>).</summary>
    public LockoutOptions Lockout { get; } = new();
}
