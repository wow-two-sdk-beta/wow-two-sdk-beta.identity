namespace WoW.Two.Sdk.Beta.Be.Identity.Core.InviteSignUp;

/// <summary>Represents an invitation sign-up verified with its trusted contact's code.</summary>
public sealed record InviteSignUpVerifyResult
{
    /// <summary>Gets how it ended.</summary>
    public required InviteSignUpVerifyStatus Status { get; init; }

    /// <summary>Gets why the join was refused, an identity error code, for <see cref="InviteSignUpVerifyStatus.Failed"/>.</summary>
    public string? FailureCode { get; init; }

    /// <summary>Gets the new person to sign the browser in as (<see cref="InviteSignUpVerifyStatus.Completed"/>).</summary>
    public IdentityUser? SignInAs { get; init; }

    /// <summary>Gets the trusted contact whose code proved the sign-up.</summary>
    public Guid? TrustedContactUserId { get; init; }

    /// <summary>Gets the app the sign-up serves, for the sign-in log.</summary>
    public string? ClientId { get; init; }

    internal static InviteSignUpVerifyResult Of(InviteSignUpVerifyStatus status, string? failureCode = null) => new() { Status = status, FailureCode = failureCode };
}
