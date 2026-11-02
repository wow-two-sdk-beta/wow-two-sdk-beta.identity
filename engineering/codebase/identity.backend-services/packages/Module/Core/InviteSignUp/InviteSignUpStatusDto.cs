namespace WoW.Two.Sdk.Beta.Be.Identity.Core.InviteSignUp;

/// <summary>Represents where an invitation sign-up stands for the browser that started it.</summary>
public sealed record InviteSignUpStatusDto
{
    /// <summary>Gets <c>pending</c>, <c>completed</c>, <c>failed</c> or <c>expired</c>.</summary>
    public required string Status { get; init; }

    /// <summary>Gets why the join was refused, an identity error code; null unless <c>failed</c>.</summary>
    public string? FailureCode { get; init; }

    /// <summary>Gets whether the browser is signed in: true once <c>completed</c> signed it in; false when the person must sign in themselves, such as with a second factor.</summary>
    public bool IsSignedIn { get; init; }
}
