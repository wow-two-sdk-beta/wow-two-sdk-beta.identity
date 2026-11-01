namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Endpoints;

/// <summary>Represents the session's active organization (<c>org_id</c>) and the person's role in it (<c>org_role</c>).</summary>
public sealed record BffOrganizationDto
{
    /// <summary>Gets the organization's id.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the person's role, such as <c>owner</c>, or null when the token names none.</summary>
    public required string? Role { get; init; }
}
