namespace WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;

/// <summary>A sign-in waiting for a trusted contact's press on a new device.</summary>
public sealed record DeviceConfirmationDto
{
    /// <summary>The token <c>phone-sign-in/confirmation</c> polls with.</summary>
    public required string Token { get; init; }

    /// <summary>When the wait ends (UTC).</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>The masked labels of the contacts asked.</summary>
    public required IReadOnlyList<string> Contacts { get; init; }
}
