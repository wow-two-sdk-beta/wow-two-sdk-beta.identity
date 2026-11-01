namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInDeclaration;

/// <summary>One enabled sign-in method: its id, the channels its codes travel over, and the fields its form renders.</summary>
public sealed record SignInMethodDto
{
    /// <summary>The method (<see cref="SignInMethodIdConstants"/>), such as <c>password</c> or <c>phone-code</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The channels a code goes out over, such as <c>telegram</c>; empty for a method without codes.</summary>
    public IReadOnlyList<string> Channels { get; init; } = [];

    /// <summary>The sign-in form's fields, in order, each named as the sign-in request's property.</summary>
    public required IReadOnlyList<SignInFieldDto> Fields { get; init; }

    /// <summary>
    /// The rules a new password meets on sign-up, reset and change: the password method only, null for any other. The
    /// sign-in form's own password field only requires a value, since a stored password the rules now refuse still signs in
    /// once, to its change.
    /// </summary>
    public IReadOnlyList<SignInRuleDto>? PasswordRules { get; init; }
}
