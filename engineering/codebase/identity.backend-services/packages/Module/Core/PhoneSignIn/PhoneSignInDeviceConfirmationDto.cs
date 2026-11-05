namespace WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;

/// <summary>Answers <c>phone-sign-in</c> with no session on a new device of an account with a deliverable contact.</summary>
public sealed record PhoneSignInDeviceConfirmationDto
{
    /// <summary>The wait; complete it with <c>phone-sign-in/confirmation</c>.</summary>
    public required DeviceConfirmationDto DeviceConfirmation { get; init; }
}
