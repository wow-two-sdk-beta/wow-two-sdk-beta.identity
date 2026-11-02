namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Endpoints;

/// <summary>Represents the session's active organization (<c>org_id</c>, <c>org_name</c>, <c>org_slug</c>) and the person's role in it (<c>org_role</c>).</summary>
public sealed record BffOrganizationDto
{
    /// <summary>Gets the organization's id.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the organization's display name (<c>org_name</c>), or null when the token names none.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the organization's handle (<c>org_slug</c>), or null when the token names none.</summary>
    public string? Slug { get; init; }

    /// <summary>Gets the person's role, such as <c>owner</c>, or null when the token names none.</summary>
    public required string? Role { get; init; }
}
