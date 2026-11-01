namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

/// <summary>Asks for the confirmation of a password change: a code or link sent to the person's verified channel.</summary>
public sealed record PasswordChangeConfirmationApiRequest
{
    /// <summary>The ticket a sign-in answered with when the password breaks the current rules; omit when signed in.</summary>
    public string? Ticket { get; init; }
}
