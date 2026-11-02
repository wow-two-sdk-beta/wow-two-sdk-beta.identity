namespace Identity.Api.Console.Models;

/// <summary>Represents an organization the console's admin creates, becoming its owner.</summary>
public sealed record CreateOrganizationApiRequest
{
    /// <summary>Gets the organization's display name, 1–120 characters.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the organization's handle: 1–64 lowercase letters, digits and single hyphens, such as <c>ocharo</c>.</summary>
    public string? Slug { get; init; }

    /// <summary>Gets the organization's id to keep, such as a product's existing key; absent or the empty uuid assigns a uuid v7.</summary>
    public Guid? Id { get; init; }
}
