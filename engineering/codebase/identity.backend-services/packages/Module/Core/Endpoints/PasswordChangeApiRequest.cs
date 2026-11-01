namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

/// <summary>Changes the password of a person who is signed in, or holds the ticket a sign-in answered with.</summary>
public sealed record PasswordChangeApiRequest
{
    /// <summary>The current password; not asked of a person holding the ticket, whose sign-in just verified it.</summary>
    public string? CurrentPassword { get; init; }

    /// <summary>The replacement password.</summary>
    public required string NewPassword { get; init; }

    /// <summary>The code that was sent, when the instance confirms a change with a code.</summary>
    public string? Code { get; init; }

    /// <summary>The ticket a sign-in answered with when the password breaks the current rules; omit when signed in.</summary>
    public string? Ticket { get; init; }
}
