namespace WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;

/// <summary>Represents the outcome of a phone sign-in step.</summary>
public sealed record PhoneSignInResult
{
    /// <summary>How the step ended.</summary>
    public required PhoneSignInStatus Status { get; init; }

    /// <summary>The number in E.164, once it was read.</summary>
    public string? PhoneNumber { get; init; }

    /// <summary>The <c>t.me</c> start link to open (<see cref="PhoneSignInStatus.LinkRequired"/>).</summary>
    public string? TelegramLinkUrl { get; init; }

    /// <summary>The person the code proved (<see cref="PhoneSignInStatus.Verified"/>); complete with <c>SignInService.SignInAsync</c>.</summary>
    public IdentityUser? User { get; init; }

    /// <summary>Whether this step created <see cref="User"/>.</summary>
    public bool IsUserCreated { get; init; }

    /// <summary>The trusted contact whose code proved <see cref="User"/>; null when the number's own chat received it.</summary>
    public Guid? TrustedContactUserId { get; init; }

    /// <summary>A result with <paramref name="status"/> only.</summary>
    /// <param name="status">How the step ended.</param>
    /// <param name="phoneNumber">The number in E.164, when read.</param>
    public static PhoneSignInResult Of(PhoneSignInStatus status, string? phoneNumber = null) => new() { Status = status, PhoneNumber = phoneNumber };
}
