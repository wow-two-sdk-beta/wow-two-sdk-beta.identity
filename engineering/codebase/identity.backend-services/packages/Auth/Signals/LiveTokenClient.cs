using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2;

namespace WoW.Two.Sdk.Beta.Be.Identity.Signals;

/// <summary>Asks Identity about one token over RFC 7662, on each call: nothing is cached, and only a plain <c>active: true</c> counts.</summary>
/// <param name="clients">Creates the HTTP client.</param>
/// <param name="options">The endpoint and the client's credentials.</param>
/// <param name="time">The clock an assertion is signed by.</param>
/// <param name="logger">Records an endpoint that didn't answer.</param>
internal sealed partial class LiveTokenClient(IHttpClientFactory clients, IOptions<LiveTokenCheckOptions> options, TimeProvider time, ILogger<LiveTokenClient> logger)
{
    /// <summary>The named HTTP client, which a host may configure.</summary>
    public const string HttpClientName = "identity-live-token";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Whether the instance still stands behind <paramref name="token"/>; an endpoint that doesn't answer says no.</summary>
    /// <param name="token">The access token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> IsActiveAsync(string token, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        var settings = options.Value;
        var endpoint = settings.IntrospectionEndpoint!;
        var form = new List<KeyValuePair<string, string>> { new("token", token), new("token_type_hint", "access_token") };
        OAuth2ClientAuthentication.AddTo(form, settings, endpoint, time.GetUtcNow());

        try
        {
            using var client = CreateClient(settings);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new FormUrlEncodedContent(form) };
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogFailed(logger, ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
                return false;
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var answer = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            return answer.RootElement.ValueKind == JsonValueKind.Object
                && answer.RootElement.TryGetProperty("active", out var active)
                && active.ValueKind == JsonValueKind.True;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or OperationCanceledException)
        {
            LogFailed(logger, exception.GetType().Name);
            return false;
        }
    }

    private HttpClient CreateClient(LiveTokenCheckOptions settings)
    {
        var client = settings.BackchannelHttpHandler is { } handler ? new HttpClient(handler, disposeHandler: false) : clients.CreateClient(HttpClientName);
        client.Timeout = RequestTimeout;
        return client;
    }

    [LoggerMessage(EventId = 7432, Level = LogLevel.Warning, Message = "Introspection didn't answer ({Reason}); the live token check fails closed")]
    private static partial void LogFailed(ILogger logger, string reason);
}
