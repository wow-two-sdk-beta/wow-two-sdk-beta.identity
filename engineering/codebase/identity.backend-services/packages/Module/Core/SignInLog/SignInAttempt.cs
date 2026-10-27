namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInLog;

/// <summary>Carries one sign-in attempt to the log; the service masks the subject and adds the request's address, user agent and trace.</summary>
public sealed record SignInAttempt
{
    /// <summary>The step attempted, one of <see cref="SignInMethodConstants"/>.</summary>
    public required string Method { get; init; }

    /// <summary>How it ended, one of <see cref="SignInResultConstants"/>.</summary>
    public required string Result { get; init; }

    /// <summary>Why it did not succeed, one of <see cref="SignInReasonConstants"/>; null on success.</summary>
    public string? Reason { get; init; }

    /// <summary>The account the attempt named, when one exists.</summary>
    public Guid? UserId { get; init; }

    /// <summary>The login as typed; stored masked, never in full.</summary>
    public string? Subject { get; init; }
}
