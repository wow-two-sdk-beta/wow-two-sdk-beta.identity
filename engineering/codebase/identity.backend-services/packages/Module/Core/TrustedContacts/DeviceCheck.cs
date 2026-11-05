namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>The outcome of <see cref="DeviceConfirmationService.CheckAsync"/>.</summary>
public sealed record DeviceCheck
{
    /// <summary>Sign in.</summary>
    public static readonly DeviceCheck Passed = new() { Status = DeviceCheckStatus.Passed };

    /// <summary>Every contact's chat refused the notice.</summary>
    public static readonly DeviceCheck Unavailable = new() { Status = DeviceCheckStatus.Unavailable };

    /// <summary>What the code may do.</summary>
    public required DeviceCheckStatus Status { get; init; }

    /// <summary>The token the page polls with (<see cref="DeviceCheckStatus.Waiting"/>).</summary>
    public string? Token { get; init; }

    /// <summary>When the wait ends (<see cref="DeviceCheckStatus.Waiting"/>).</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>The masked labels of the contacts asked (<see cref="DeviceCheckStatus.Waiting"/>).</summary>
    public IReadOnlyList<string> Contacts { get; init; } = [];
}
