using Identity.Application.Status.Queries;
using WoW.Two.Sdk.Beta.Be.Mediator;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Identity.Api.Status.Models;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;

namespace Identity.Api.Status.Controllers;

/// <summary>Exposes system status over HTTP.</summary>
[ApiController]
[Route("api/system")]
public sealed class SystemController(ISender sender) : ControllerBase
{
    /// <summary>Holds the name the service reports.</summary>
    private const string ServiceName = "identity";

    /// <summary>Reports service liveness.</summary>
    /// <returns>The service name, liveness state and running version.</returns>
    [HttpGet("status")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<SystemStatusDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Status(CancellationToken cancellationToken)
    {
        var result = await sender.SendAsync(new GetSystemStatusQuery(), cancellationToken);

        return result.Match<IActionResult>(
            success => Ok(ApiResponse<SystemStatusDto>.Ok(new SystemStatusDto { Service = ServiceName, Status = "ok", Version = success.Data.Version })),
            failure => this.ToProblem(failure.Error));
    }
}
