namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Roles;

/// <summary>Represents how giving a person an instance role ended.</summary>
public sealed record InstanceRoleGrant
{
    /// <summary>Gets success, or the refusal with its code.</summary>
    public required IdentityResult Result { get; init; }

    /// <summary>Gets the role in the catalog's spelling; null when the name is not in the catalog.</summary>
    public string? Role { get; init; }

    /// <summary>Gets whether this call gave the role; false when the person held it already or it was refused.</summary>
    public bool IsGranted { get; init; }

    /// <summary>Gets whether the person holds the role now.</summary>
    public bool IsSuccess => Result.IsSuccess;
}
