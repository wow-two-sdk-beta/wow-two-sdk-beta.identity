namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

/// <summary>How the instance confirms a password change, and whether a code or link went out.</summary>
public sealed record PasswordChangeConfirmationDto
{
    /// <summary>The confirmation, <c>None</c>, <c>Code</c> or <c>Link</c>.</summary>
    public required string Confirmation { get; init; }

    /// <summary>Whether a code or link was sent; always false under <c>None</c>.</summary>
    public required bool IsSent { get; init; }
}
