using Microsoft.AspNetCore.Authorization;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Roles;

namespace Identity.Api.Console.Authorization;

/// <summary>
/// Meets <see cref="InstanceRoleRequirement"/> when the person the sign-in names still exists and holds the role in the
/// store now. A role the cookie carries counts for nothing: it reflects the sign-in, not the store.
/// </summary>
/// <param name="accounts">Finds the signed-in person.</param>
/// <param name="roles">Reads the person's instance roles.</param>
/// <param name="core">The claim type the person's id travels under.</param>
public sealed class InstanceRoleAuthorizationHandler(
    UserAccountService accounts,
    InstanceRoleService roles,
    IdentityCoreOptions core) : AuthorizationHandler<InstanceRoleRequirement>
{
    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, InstanceRoleRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);
        if (!Guid.TryParse(context.User.FindFirst(core.Claims.UserIdClaimType)?.Value, out var userId))
            return;

        var cancellationToken = context.Resource is HttpContext http ? http.RequestAborted : CancellationToken.None;
        if (await accounts.FindByIdAsync(userId, cancellationToken) is not { } user)
            return;

        var held = await roles.GetAsync(user, cancellationToken);
        if (held.Contains(requirement.Role, StringComparer.OrdinalIgnoreCase))
            context.Succeed(requirement);
    }
}
