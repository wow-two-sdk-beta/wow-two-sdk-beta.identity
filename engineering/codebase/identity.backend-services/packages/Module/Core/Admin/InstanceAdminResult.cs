namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Admin;

/// <summary>Represents how creating the instance's first admin ended.</summary>
public sealed record InstanceAdminResult
{
    /// <summary>Gets the outcome.</summary>
    public required InstanceAdminStatus Status { get; init; }

    /// <summary>Gets the named person's id once they exist; null when invalid, or refused before they were found.</summary>
    public Guid? UserId { get; init; }

    /// <summary>Gets whether this run created the person.</summary>
    public bool IsUserCreated { get; init; }

    /// <summary>Gets whether this run gave the person the instance role <c>admin</c>; false when they held it already.</summary>
    public bool IsRoleGranted { get; init; }

    /// <summary>Gets why a run was refused or invalid; null otherwise.</summary>
    public string? Error { get; init; }

    internal static InstanceAdminResult Refused(string error, Guid? userId = null)
        => new() { Status = InstanceAdminStatus.Refused, Error = error, UserId = userId };

    internal static InstanceAdminResult Invalid(string error) => new() { Status = InstanceAdminStatus.Invalid, Error = error };
}
