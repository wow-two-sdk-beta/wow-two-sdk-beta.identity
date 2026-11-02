namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Admin;

/// <summary>Represents the instance's first admin, as <c>admin create</c> names them.</summary>
public sealed record InstanceAdminRequest
{
    /// <summary>Gets the name the admin goes by, 1–120 characters.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Gets the admin's phone, as typed; the admin needs a phone, an email or both.</summary>
    public string? PhoneNumber { get; init; }

    /// <summary>Gets the admin's email; the admin needs a phone, an email or both.</summary>
    public string? Email { get; init; }

    /// <summary>Gets the admin's id to keep, such as a product's existing key; null assigns a uuid v7.</summary>
    public Guid? UserId { get; init; }
}
