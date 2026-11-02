namespace WoW.Two.Sdk.Beta.Be.Identity.Core.InviteSignUp;

/// <summary>Represents one poll of an invitation sign-up by the browser that started it.</summary>
public sealed record InviteSignUpPoll
{
    /// <summary>Gets where the sign-up stands.</summary>
    public required InviteSignUpState State { get; init; }

    /// <summary>Gets why the join was refused, an identity error code, for <see cref="InviteSignUpState.Failed"/>.</summary>
    public string? FailureCode { get; init; }

    /// <summary>Gets the person to sign the browser in as: set on the first poll after the join only.</summary>
    public IdentityUser? SignInAs { get; init; }

    /// <summary>Gets the app the sign-up serves, for the sign-in log.</summary>
    public string? ClientId { get; init; }

    internal static InviteSignUpPoll Of(InviteSignUpState state, string? failureCode = null) => new() { State = state, FailureCode = failureCode };
}
