using Identity.Application.Console.Commands;
using Identity.Application.Console.Models;
using Identity.Application.Console.Queries;
using WoW.Two.Sdk.Beta.Be.Mediator;
using Identity.Api.Configurations;
using Identity.Api.Console.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Factories;

namespace Identity.Api.Console.Controllers;

/// <summary>
/// Exposes the console's session: who is signed in, which instance roles open its areas, and the organizations they
/// manage; and, for the instance's admins, organization creation. The console opens to a person holding an instance role
/// or owning or administering an organization; its instance-wide parts stay behind the instance role. Roles and
/// memberships are read from the store on every request, never from the cookie, so a grant or revoke counts from the
/// next request.
/// </summary>
[ApiController]
[Route("api/console")]
[Authorize]
public sealed class ConsoleController(ISender sender, IdentityCoreOptions core) : ControllerBase
{
    /// <summary>
    /// Reports the signed-in person: <c>401</c> without a sign-in, <c>403</c> for a person who neither holds an instance
    /// role nor owns or administers an organization. The instance roles and the organizations are read from the store, so
    /// a role granted or revoked after the sign-in shows on the next request.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The person's id, name, instance roles and managed organizations.</returns>
    [HttpGet("session")]
    [ProducesResponseType<ApiResponse<ConsoleSessionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Session(CancellationToken cancellationToken)
    {
        var result = await sender.SendAsync(new GetConsoleSessionQuery(CallerId()), cancellationToken);

        return result.Match<IActionResult>(success => Ok(ApiResponse<ConsoleSessionDto>.Ok(new ConsoleSessionDto
        {
            UserId = success.Data.UserId.ToString("D"),
            Name = success.Data.Name,
            Roles = success.Data.Roles,
            Organizations = success.Data.Organizations.Select(ToDto).ToArray(),
        })), failure => AnswerFailure(failure.Error));
    }

    /// <summary>
    /// Creates an organization with the signed-in admin as its owner, after which it appears in the session's
    /// organizations; a preset id keeps a product's existing key. <c>401</c> without a sign-in, <c>403</c> without the
    /// instance role <c>admin</c> in the store now (<c>support</c> and <c>auditor</c> included), whatever the sign-in carried.
    /// </summary>
    /// <remarks>
    /// Refusals carry their code in the problem's <c>identityCode</c>: <c>400</c> <c>OrganizationNameRequired</c> or
    /// <c>InvalidOrganizationSlug</c>; <c>409</c> <c>DuplicateOrganizationSlug</c> for a taken handle or
    /// <c>DuplicateOrganizationId</c> for a taken id.
    /// </remarks>
    /// <param name="request">The organization's name, handle and optional id.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The organization as the session lists it, the admin as its owner.</returns>
    [HttpPost("organizations")]
    [Authorize(Policy = HostConfiguration.ConsoleAdminPolicy)]
    [ProducesResponseType<ApiResponse<ConsoleOrganizationDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateOrganization([FromBody] CreateOrganizationApiRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateConsoleOrganizationCommand(CallerId(), request.Id, request.Name, request.Slug);
        var result = await sender.SendAsync(command, cancellationToken);

        return result.Match<IActionResult>(
            success => StatusCode(StatusCodes.Status201Created, ApiResponse<ConsoleOrganizationDto>.Ok(ToDto(success.Data))),
            failure => AnswerFailure(failure.Error));
    }

    private Guid? CallerId() =>
        Guid.TryParse(User.FindFirst(core.Claims.UserIdClaimType)?.Value, out var id) ? id : null;

    private IActionResult AnswerFailure(AppError error) => error.Type switch
    {
        AppErrorType.Unauthorized => Unauthorized(),
        AppErrorType.Forbidden => Forbid(),
        _ => throw error.ToException(),
    };

    private static ConsoleOrganizationDto ToDto(ConsoleOrganization organization) => new()
    {
        Id = organization.Id.ToString("D"),
        Name = organization.Name,
        Slug = organization.Slug,
        Role = organization.Role,
    };
}
