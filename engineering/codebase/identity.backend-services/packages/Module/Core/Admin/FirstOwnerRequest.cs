namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Admin;

/// <summary>Represents the first organization of an instance and its owner, as <c>admin create</c> names them.</summary>
public sealed record FirstOwnerRequest
{
    /// <summary>Gets the organization's handle, such as <c>ocharo</c>.</summary>
    public required string Slug { get; init; }

    /// <summary>Gets the organization's display name.</summary>
    public required string OrganizationName { get; init; }

    /// <summary>Gets the organization's id to keep, such as a product's existing key; null assigns a uuid v7.</summary>
    public Guid? OrganizationId { get; init; }

    /// <summary>Gets the owner's phone, as typed; the owner needs a phone, an email or both.</summary>
    public string? PhoneNumber { get; init; }

    /// <summary>Gets the owner's email; the owner needs a phone, an email or both.</summary>
    public string? Email { get; init; }

    /// <summary>Gets the name the owner goes by, 1–120 characters.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Gets the owner's id to keep, such as a product's existing key; null assigns a uuid v7.</summary>
    public Guid? UserId { get; init; }

    /// <summary>Gets the instance role the owner also holds, such as <c>admin</c> for the console; null gives none.</summary>
    public string? InstanceRole { get; init; }
}
