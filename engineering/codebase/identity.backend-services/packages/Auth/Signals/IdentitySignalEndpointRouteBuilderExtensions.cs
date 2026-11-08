using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Signals;
using WoW.Two.Sdk.Beta.Be.Identity.Jwt;
using WoW.Two.Sdk.Beta.Be.Messaging;
using WoW.Two.Sdk.Beta.Be.Messaging.Buses;
using WoW.Two.Sdk.Beta.Be.Messaging.Webhooks.Inbound;

namespace WoW.Two.Sdk.Beta.Be.Identity.Signals;

/// <summary>Maps the route that hears Identity's revocation signals.</summary>
public static class IdentitySignalEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Map <c>POST <paramref name="path"/></c>: open to anyone, since the signature is the check. A verified signal goes onto the
    /// local <see cref="IEventBus"/> and answers <c>202</c>; a failed signature answers <c>401</c>; an event type this version
    /// doesn't know answers <c>202</c> and is ignored, so a newer sender doesn't fail; a repeated delivery id answers without
    /// running again. The route stays out of API descriptions.
    /// </summary>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="path">The route, whose public URL is the signal URL registered at the issuer.</param>
    /// <returns>The route's builder.</returns>
    /// <exception cref="InvalidOperationException"><c>AddIdentitySignalReceiver</c> did not name a signal URL and an issuer, listed or resolved.</exception>
    public static RouteHandlerBuilder MapIdentitySignals(this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string path)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var hasResolver = endpoints.ServiceProvider.GetRequiredService<IServiceProviderIsService>().IsService(typeof(ITrustedIssuerResolver));
        if (!endpoints.ServiceProvider.GetRequiredService<IOptions<IdentitySignalReceiverOptions>>().Value.IsConfiguredWith(hasResolver))
            throw new InvalidOperationException($"{nameof(MapIdentitySignals)} needs AddIdentitySignalReceiver with a signal URL and a trusted issuer, listed or resolved.");

        return endpoints.MapPost(path, ReceiveAsync)
            .AddEndpointFilter<RouteHandlerBuilder, IdentitySignalGateFilter>()
            .RequireWebhookSignature(IdentitySignalReceiverConstants.ReceiverName)
            .AllowAnonymous()
            .ExcludeFromDescription();
    }

    private static async Task<IResult> ReceiveAsync(WebhookReceiptModel receipt, [FromServices] IEventBus bus, CancellationToken cancellationToken)
    {
        try
        {
            return receipt.EventType switch
            {
                nameof(SessionEndedEvent) => await PublishAsync<SessionEndedEvent>(receipt, bus, cancellationToken),
                nameof(AccessChangedEvent) => await PublishAsync<AccessChangedEvent>(receipt, bus, cancellationToken),
                _ => Results.Accepted(),
            };
        }
        catch (JsonException)
        {
            return Unreadable();
        }
    }

    private static async Task<IResult> PublishAsync<TEvent>(WebhookReceiptModel receipt, IEventBus bus, CancellationToken cancellationToken)
        where TEvent : class, IEvent
    {
        if (receipt.ReadJson<TEvent>() is not { } signal)
            return Unreadable();

        await bus.PublishAsync(signal, new PublishOptions { MessageId = receipt.DeliveryId }, cancellationToken);
        return Results.Accepted();
    }

    /// <summary>A signature that verifies over a body this event can't be read from: permanent, so the sender doesn't retry it.</summary>
    private static IResult Unreadable() => Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The signal's body can't be read.");
}
