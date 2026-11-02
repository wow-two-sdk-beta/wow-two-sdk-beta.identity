using Identity.Api.Configurations;
using Identity.Api.Console.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Roles;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;

namespace Identity.Api.Console.Controllers;

/// <summary>Exposes the console's session: who is signed in and which instance roles open its areas.</summary>
/// <param name="accounts">Finds the signed-in person.</param>
/// <param name="instanceRoles">Reads their instance roles.</param>
/// <param name="core">The claim type the person's id travels under.</param>
[ApiController]
[Route("api/console")]
[Authorize(Policy = HostConfiguration.ConsolePolicy)]
public sealed class ConsoleController(UserAccountService accounts, InstanceRoleService instanceRoles, IdentityCoreOptions core) : ControllerBase
{
    /// <summary>Reports the signed-in person; <c>401</c> without a sign-in, <c>403</c> without an instance role.</summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The person's id, name and instance roles.</returns>
    [HttpGet("session")]
    [ProducesResponseType<ApiResponse<ConsoleSessionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Session(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst(core.Claims.UserIdClaimType)?.Value, out var userId)
            || await accounts.FindByIdAsync(userId, cancellationToken) is not { } user)
        {
            return Unauthorized();
        }

        var name = new[] { user.DisplayName, user.PhoneNumber, user.UserName }.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? user.Id.ToString("D");
        var session = new ConsoleSessionDto
        {
            UserId = user.Id.ToString("D"),
            Name = name.Trim(),
            Roles = await instanceRoles.GetAsync(user, cancellationToken),
        };
        return Ok(ApiResponse<ConsoleSessionDto>.Ok(session));
    }
}
