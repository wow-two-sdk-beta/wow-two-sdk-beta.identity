using Microsoft.AspNetCore.Mvc;
using WoW.Two.Sdk.Beta.Be.Identity.Greetings;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;

namespace Identity.Api.Greetings.Controllers;

/// <summary>Exposes the greetings contract over HTTP — the mirror the Client package calls.</summary>
[ApiController]
[Route(GreetingRouteConstants.Greetings)]
public sealed class GreetingsController(IGreetingsBroker broker) : ControllerBase
{
    /// <summary>Greets <paramref name="name"/>, or the world when it is omitted.</summary>
    /// <param name="name">The name to greet.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpGet]
    [ProducesResponseType<ApiResponse<GreetingModel>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get([FromQuery(Name = GreetingRouteConstants.NameParameter)] string? name, CancellationToken ct)
    {
        var result = await broker.GreetAsync(name, ct);

        return result.Match<IActionResult>(
            greeting => Ok(ApiResponse<GreetingModel>.Ok(greeting)),
            error => this.ToProblem(error));
    }
}
