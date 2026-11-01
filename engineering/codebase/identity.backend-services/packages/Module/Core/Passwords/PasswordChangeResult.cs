namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;

/// <summary>Represents the outcome of a password change.</summary>
public sealed record PasswordChangeResult
{
    /// <summary>Whether the password changed.</summary>
    public required bool IsSuccess { get; init; }

    /// <summary>The failures when <see cref="IsSuccess"/> is false; empty otherwise.</summary>
    public IReadOnlyList<IdentityError> Errors { get; init; } = [];

    /// <summary>The person whose password changed; set on success.</summary>
    public IdentityUser? User { get; init; }

    /// <summary>Whether the session the change was made in stays signed in with the new password; false when every session ended.</summary>
    public bool IsSessionKept { get; init; }

    /// <summary>The failures as an identity result, for <c>ToValidationError</c>.</summary>
    public IdentityResult ToIdentityResult() => IsSuccess ? IdentityResult.Success : IdentityResult.Failed([.. Errors]);

    internal static PasswordChangeResult Failed(IdentityResult failure)
        => new() { IsSuccess = false, Errors = failure.Errors };

    internal static PasswordChangeResult Failed(string code, string description)
        => new() { IsSuccess = false, Errors = [new IdentityError { Code = code, Description = description }] };
}
