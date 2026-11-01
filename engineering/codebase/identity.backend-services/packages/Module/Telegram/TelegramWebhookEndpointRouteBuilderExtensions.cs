using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace WoW.Two.Sdk.Beta.Be.Identity.Telegram;

/// <summary>Maps the bot's webhook.</summary>
public static class TelegramWebhookEndpointRouteBuilderExtensions
{
    /// <summary>The header Telegram carries the webhook's <c>secret_token</c> in.</summary>
    public const string SecretHeaderName = "X-Telegram-Bot-Api-Secret-Token";

    /// <summary>The route the instance's bot posts updates to, by default.</summary>
    public const string DefaultPattern = "/api/telegram/webhook";

    /// <summary>
    /// Maps <c>POST {pattern}</c> for the bot's updates: <c>404</c> while <see cref="TelegramBotOptions.IsEnabled"/> is off or
    /// <see cref="TelegramBotOptions.UpdateMode"/> is <see cref="TelegramUpdateMode.Polling"/>, <c>401</c> unless <see cref="SecretHeaderName"/> equals <see cref="TelegramBotOptions.WebhookSecret"/>, <c>400</c> for
    /// a body that is no update, else <c>200</c> once <see cref="TelegramUpdateHandler"/> acted on it. A deployed boot registers
    /// the URL and the secret with Telegram's <c>setWebhook</c> itself (<see cref="TelegramWebhookRegistrationService"/>).
    /// </summary>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="pattern">The route; default <see cref="DefaultPattern"/>.</param>
    public static IEndpointConventionBuilder MapTelegramWebhook(this IEndpointRouteBuilder endpoints, string pattern = DefaultPattern)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        return endpoints.MapPost(pattern, ReceiveAsync).ExcludeFromDescription();
    }

    private static async Task<IResult> ReceiveAsync(HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        var options = services.GetRequiredService<IOptionsMonitor<TelegramBotOptions>>().CurrentValue;
        if (!options.IsEnabled || options.UpdateMode == TelegramUpdateMode.Polling)
            return Results.NotFound();
        if (!IsSecret(http.Request.Headers[SecretHeaderName].ToString(), options.WebhookSecret))
            return Results.Unauthorized();

        Update? update;
        try
        {
            update = await JsonSerializer.DeserializeAsync<Update>(http.Request.Body, JsonBotAPI.Options, cancellationToken);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }

        if (update is not null)
            await services.GetRequiredService<TelegramUpdateHandler>().HandleAsync(update, cancellationToken);

        return Results.Ok();
    }

    private static bool IsSecret(string presented, string? expected)
        => !string.IsNullOrEmpty(expected)
           && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(expected));
}
