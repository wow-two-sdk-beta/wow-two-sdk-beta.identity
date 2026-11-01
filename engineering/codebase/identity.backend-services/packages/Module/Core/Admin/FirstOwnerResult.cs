namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Admin;

/// <summary>Represents how creating the first organization and its owner ended.</summary>
public sealed record FirstOwnerResult
{
    /// <summary>Gets the outcome.</summary>
    public required FirstOwnerStatus Status { get; init; }

    /// <summary>Gets the organization's id once it exists; null when refused or invalid before it was found.</summary>
    public Guid? OrganizationId { get; init; }

    /// <summary>Gets the owner's id once they exist; null when refused or invalid before they were found.</summary>
    public Guid? UserId { get; init; }

    /// <summary>Gets whether this run created the organization.</summary>
    public bool IsOrganizationCreated { get; init; }

    /// <summary>Gets whether this run created the owner.</summary>
    public bool IsOwnerCreated { get; init; }

    /// <summary>Gets why a run was refused or invalid; null otherwise.</summary>
    public string? Error { get; init; }

    internal static FirstOwnerResult Refused(string error, Guid? organizationId = null, Guid? userId = null)
        => new() { Status = FirstOwnerStatus.Refused, Error = error, OrganizationId = organizationId, UserId = userId };

    internal static FirstOwnerResult Invalid(string error) => new() { Status = FirstOwnerStatus.Invalid, Error = error };
}
