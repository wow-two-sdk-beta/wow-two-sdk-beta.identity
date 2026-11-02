using Microsoft.AspNetCore.Authorization;

namespace Identity.Api.Console.Authorization;

/// <summary>
/// Requires the signed-in person to hold an instance role now: <see cref="InstanceRoleAuthorizationHandler"/> reads it from
/// the store on every request, never from the cookie's claims, so a grant or revoke counts from the next request.
/// </summary>
/// <param name="role">One of <c>InstanceRoleConstants</c>.</param>
public sealed class InstanceRoleRequirement(string role) : IAuthorizationRequirement
{
    /// <summary>Gets the instance role the person must hold.</summary>
    public string Role { get; } = role;
}
