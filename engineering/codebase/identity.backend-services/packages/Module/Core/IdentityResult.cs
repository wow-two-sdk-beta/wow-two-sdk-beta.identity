namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>Represents the outcome of an identity operation — success, or a list of failures.</summary>
public sealed class IdentityResult
{
    private static readonly IdentityResult SuccessResult = new(true, []);

    private IdentityResult(bool isSuccess, IReadOnlyList<IdentityError> errors)
    {
        IsSuccess = isSuccess;
        Errors = errors;
    }

    /// <summary>Whether the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>The failures when <see cref="IsSuccess"/> is false; empty otherwise.</summary>
    public IReadOnlyList<IdentityError> Errors { get; }

    /// <summary>The shared success result.</summary>
    public static IdentityResult Success => SuccessResult;

    /// <summary>Create a failed result from one or more errors.</summary>
    /// <param name="errors">The failures.</param>
    public static IdentityResult Failed(params IdentityError[] errors) => new(false, errors ?? []);
}
