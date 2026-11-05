using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Messaging.Webhooks.Inbound;

namespace WoW.Two.Sdk.Beta.Be.Identity.Signals;

/// <summary>
/// Answers what core's webhook filter throws for a global handler to map, so the signal route answers <c>401</c> and <c>409</c> on
/// any host, whether or not its pipeline maps <see cref="AppException"/>. It wraps that filter, which still dedupes.
/// </summary>
internal sealed class IdentitySignalGateFilter : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var http = context.HttpContext;
        var receipt = await http.RequestServices.GetRequiredService<WebhookReceiverService>().ReceiveAsync(http, http.RequestAborted);

        // The signature can't be checked yet, with the issuer's discovery down: a 5xx, so the sender retries rather than drops it.
        if (http.Items.ContainsKey(IdentitySignalReceiverConstants.UncheckableItem))
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "The signal can't be checked yet.");

        if (!receipt.Verified && receipt.Failure != WebhookSignatureFailure.TooLarge)
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "The signal's signature is not valid.");

        try
        {
            return await next(context);
        }
        catch (AppException exception) when (exception.Error.Is(AppErrorType.Conflict))
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: exception.Message);
        }
    }
}
