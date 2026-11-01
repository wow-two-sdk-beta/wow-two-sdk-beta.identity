namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Sessions;

/// <summary>Represents one claim of the person a session signed in, as the instance's ID token carried it.</summary>
internal sealed record BffSessionClaim
{
    /// <summary>Gets the claim type, such as <c>sub</c> or <c>org_permission</c>.</summary>
    public required string Type { get; init; }

    /// <summary>Gets the claim value.</summary>
    public required string Value { get; init; }

    /// <summary>Gets the claim's value type, such as <c>http://www.w3.org/2001/XMLSchema#boolean</c>.</summary>
    public required string ValueType { get; init; }
}
