using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Identity.Api.Status.Models;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;

namespace Identity.Api.Status.Controllers;

/// <summary>Exposes system status over HTTP.</summary>
[ApiController]
[Route("api/system")]
public sealed class SystemController : ControllerBase
{
    /// <summary>Holds the name the service reports.</summary>
    private const string ServiceName = "identity";

    /// <summary>Reports service liveness.</summary>
    /// <returns>The service name, liveness state and running version.</returns>
    [HttpGet("status")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<SystemStatusDto>>(StatusCodes.Status200OK)]
    public IActionResult Status()
    {
        var version = typeof(SystemController).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        var status = new SystemStatusDto { Service = ServiceName, Status = "ok", Version = version };

        return Ok(ApiResponse<SystemStatusDto>.Ok(status));
    }
}
