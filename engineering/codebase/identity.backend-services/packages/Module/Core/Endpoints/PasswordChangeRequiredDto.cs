namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

/// <summary>The sign-in's answer when the password matched but breaks the current rules: no session or token, only the way to change it.</summary>
public sealed record PasswordChangeRequiredDto
{
    /// <summary>Always true; the field a client tests to tell this answer from a signed-in one.</summary>
    public bool IsPasswordChangeRequired { get; init; } = true;

    /// <summary>The confirmation the change needs, <c>None</c>, <c>Code</c> or <c>Link</c>.</summary>
    public required string Confirmation { get; init; }

    /// <summary>The ticket the change routes take as <c>ticket</c>; it opens them and nothing else, and a change spends it.</summary>
    public required string Ticket { get; init; }
}
