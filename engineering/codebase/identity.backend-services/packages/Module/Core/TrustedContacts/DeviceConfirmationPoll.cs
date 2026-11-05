namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>The answer to a poll of <see cref="DeviceConfirmationService.PollAsync"/>.</summary>
public sealed record DeviceConfirmationPoll
{
    /// <summary>Where the request stands.</summary>
    public required DeviceConfirmationState State { get; init; }

    /// <summary>The account to sign in (<see cref="DeviceConfirmationState.Approved"/>) or the one refused (<see cref="DeviceConfirmationState.Denied"/>).</summary>
    public IdentityUser? User { get; init; }
}
