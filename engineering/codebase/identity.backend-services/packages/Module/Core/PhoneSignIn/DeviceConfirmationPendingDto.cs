namespace WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;

/// <summary>Answers <c>phone-sign-in/confirmation</c> while nobody answered.</summary>
public sealed record DeviceConfirmationPendingDto
{
    /// <summary>Always <c>pending</c>; any other outcome has its own status code.</summary>
    public string Status { get; init; } = "pending";
}
