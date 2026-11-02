using Identity.Api.Configurations;
using Identity.Api.Console.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Roles;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Factories;

namespace Identity.Api.Console.Controllers;

/// <summary>
/// Exposes the console's session: who is signed in, which instance roles open its areas, and the organizations they
/// manage; and, for the instance's admins, organization creation. The console opens to a person holding an instance role
/// or owning or administering an organization; its instance-wide parts stay behind the instance role.
/// </summary>
/// <param name="accounts">Finds the signed-in person.</param>
/// <param name="core">The claim type the person's id travels under.</param>
/// <param name="organizations">The person's memberships, and the organizations an admin creates.</param>
[ApiController]
[Route("api/console")]
[Authorize]
public sealed class ConsoleController(
    UserAccountService accounts,
    IdentityCoreOptions core,
    OrganizationService organizations) : ControllerBase
{
    /// <summary>
    /// Reports the signed-in person: <c>401</c> without a sign-in, <c>403</c> for a person who neither holds an instance
    /// role nor owns or administers an organization. The instance roles are those the sign-in carries, so an added role
    /// counts from the next sign-in; the organizations are read live.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The person's id, name, instance roles and managed organizations.</returns>
    [HttpGet("session")]
    [ProducesResponseType<ApiResponse<ConsoleSessionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Session(CancellationToken cancellationToken)
    {
        if (await CallerAsync(cancellationToken) is not { } user)
            return Unauthorized();

        IReadOnlyList<string> roles = [.. InstanceRoleConstants.All.Where(User.IsInRole)];
        IReadOnlyList<ConsoleOrganizationDto> managed = [.. (await organizations.ListForUserAsync(user, cancellationToken))
            .Where(membership => membership.Role is OrganizationRoleConstants.Owner or OrganizationRoleConstants.Admin)
            .OrderBy(membership => membership.JoinedAt)
            .Select(membership => new ConsoleOrganizationDto
            {
                Id = membership.OrganizationId.ToString("D"),
                Name = membership.Name,
                Slug = membership.Slug,
                Role = membership.Role,
            })];
        if (roles.Count == 0 && managed.Count == 0)
            return Forbid();

        var name = new[] { user.DisplayName, user.PhoneNumber, user.UserName }.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? user.Id.ToString("D");
        var session = new ConsoleSessionDto { UserId = user.Id.ToString("D"), Name = name.Trim(), Roles = roles, Organizations = managed };
        return Ok(ApiResponse<ConsoleSessionDto>.Ok(session));
    }

    /// <summary>
    /// Creates an organization with the signed-in admin as its owner, after which it appears in the session's
    /// organizations; a preset id keeps a product's existing key. <c>401</c> without a sign-in, <c>403</c> without the
    /// instance role <c>admin</c> (<c>support</c> and <c>auditor</c> included).
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
        ArgumentNullException.ThrowIfNull(request);
        if (await CallerAsync(cancellationToken) is not { } user)
            return Unauthorized();

        var id = request.Id ?? Guid.Empty;
        if (id != Guid.Empty && await organizations.FindByIdAsync(id, cancellationToken) is { } holder)
            throw Coded(AppErrorType.Conflict, $"Organization {id} already exists, with the handle '{holder.Slug}'.", ConsoleErrorCodeConstants.DuplicateOrganizationId).ToException();

        var organization = new IdentityOrganization { Id = id, Name = request.Name ?? string.Empty, Slug = request.Slug ?? string.Empty };
        var created = await organizations.CreateAsync(organization, user, cancellationToken);
        if (!created.IsSuccess)
        {
            var refusal = created.Errors[0];
            var type = refusal.Code == IdentityErrorCodeConstants.DuplicateOrganizationSlug ? AppErrorType.Conflict : AppErrorType.Validation;
            throw Coded(type, refusal.Description, refusal.Code).ToException();
        }

        var dto = new ConsoleOrganizationDto
        {
            Id = organization.Id.ToString("D"),
            Name = organization.Name,
            Slug = organization.Slug,
            Role = await organizations.GetRoleAsync(organization, user, cancellationToken) ?? OrganizationRoleConstants.Owner,
        };
        return StatusCode(StatusCodes.Status201Created, ApiResponse<ConsoleOrganizationDto>.Ok(dto));
    }

    /// <summary>The signed-in person, or null when the cookie names nobody who still exists.</summary>
    private async Task<IdentityUser?> CallerAsync(CancellationToken cancellationToken)
        => Guid.TryParse(User.FindFirst(core.Claims.UserIdClaimType)?.Value, out var userId)
            ? await accounts.FindByIdAsync(userId, cancellationToken)
            : null;

    /// <summary>An error whose code a client reads from the problem's <c>identityCode</c> extension, beside the message key translation uses, as the account API codes them.</summary>
    private static AppError Coded(AppErrorType type, string message, string code)
    {
        var metadata = new Dictionary<string, object?>(StringComparer.Ordinal) { ["messageKey"] = code, ["identityCode"] = code };
        metadata[AppErrorProblemDetailsFactory.ExtensionsMetadataKey] = new Dictionary<string, object?>(StringComparer.Ordinal) { ["identityCode"] = code };
        return AppError.Of(type, message, metadata);
    }
}
