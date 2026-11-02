namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

/// <summary>Gives a member another role.</summary>
public sealed record MemberRoleApiRequest
{
    /// <summary>The role, one of the organization roles.</summary>
    public required string Role { get; init; }
}
