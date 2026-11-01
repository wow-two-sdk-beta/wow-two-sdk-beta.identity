using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Phones;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TestMode;

/// <summary>Maps the route that reads the messages test mode kept.</summary>
public static class TestModeEndpointRouteBuilderExtensions
{
    /// <summary>The route the kept messages are read at, by default.</summary>
    public const string DefaultPattern = "/api/test/messages";

    /// <summary>
    /// Maps <c>GET {pattern}?to=</c>, anonymous: the messages the instance sent in test mode, oldest first, in the
    /// <c>data</c> envelope; <c>404</c> unless <see cref="TestModeOptions.IsEnabled"/> is on. <c>to</c> is an email address, a
    /// phone number (the messages in the Telegram chat it is linked to) or a chat id; without it every kept message returns.
    /// </summary>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="pattern">The route; default <see cref="DefaultPattern"/>.</param>
    public static IEndpointConventionBuilder MapTestModeEndpoints(this IEndpointRouteBuilder endpoints, string pattern = DefaultPattern)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        return endpoints.MapGet(pattern, ReadAsync).ExcludeFromDescription().AllowAnonymous();
    }

    private static async Task<IResult> ReadAsync(string? to, HttpContext http, CancellationToken cancellationToken)
    {
        var services = http.RequestServices;
        if (!services.GetRequiredService<TestModeOptions>().IsEnabled)
            return Results.NotFound();

        var messages = services.GetRequiredService<TestMessageSink>().Snapshot();
        if (string.IsNullOrWhiteSpace(to))
            return Results.Ok(ApiResponse<IReadOnlyList<TestMessage>>.Ok(messages));

        var wanted = to.Trim();
        string? chat = null;
        if ((PhoneNumberPolicy.Normalize(wanted) ?? PhoneNumberPolicy.NormalizeContact(wanted)) is { } phone && services.GetService<TelegramLinkService>() is { } links
            && await links.FindChatAsync(phone, cancellationToken) is { } chatId)
        {
            chat = TestMessageSink.ChatAddress(chatId);
        }

        IReadOnlyList<TestMessage> matching =
        [
            .. messages.Where(message => message.Channel == OtpChannelNameConstants.Email
                ? string.Equals(message.To, wanted, StringComparison.OrdinalIgnoreCase)
                : message.To == wanted || (chat is not null && message.To == chat)),
        ];
        return Results.Ok(ApiResponse<IReadOnlyList<TestMessage>>.Ok(matching));
    }
}
