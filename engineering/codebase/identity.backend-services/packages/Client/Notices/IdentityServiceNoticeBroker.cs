using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Identity.Notices;

/// <summary>
/// Integrates app notices over a running Identity instance (Forms 2 and 3): <c>POST /api/identity/v1/notices</c> with the
/// client-credentials token the <see cref="HttpClient"/>'s handler adds (<c>AddOAuth2ClientCredentials</c>, the resource
/// being the instance's notice audience).
/// </summary>
/// <remarks>
///   - <c>202</c> and <c>200</c> read their body's status (<see cref="AppNoticeStatusConstants.Sent"/> or
///     <see cref="AppNoticeStatusConstants.Muted"/>); <c>404</c> <see cref="AppNoticeErrorCodeConstants.NotLinked"/> reads
///     <see cref="AppNoticeStatusConstants.NotLinked"/>
///   - any other answer, or no answer, is a failure carrying the problem's <c>identityCode</c> (or <c>http_{status}</c>), its
///     <c>reason</c>, and <c>Retry-After</c> as <c>retryAfter</c> seconds; an instance that cannot be reached reads
///     <see cref="AppNoticeErrorCodeConstants.Unavailable"/>
/// </remarks>
/// <param name="http">The client, its base address the instance's address.</param>
public sealed class IdentityServiceNoticeBroker(HttpClient http) : IAppNoticeBroker
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task<Result<AppNoticeDelivery>> SendNoticeAsync(AppNotice notice, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notice);
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync(AppNoticeConstants.Path.TrimStart('/'), notice, Json, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return Fail(AppErrorType.ExternalUnavailable, "The Identity instance could not be reached.", AppNoticeErrorCodeConstants.Unavailable, null, null);
        }

        using (response)
        {
            using var body = await ReadAsync(response, cancellationToken);
            var root = body?.RootElement;
            if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Accepted
                && root is { ValueKind: JsonValueKind.Object } envelope
                && envelope.TryGetProperty("data", out var data)
                && data.Deserialize<AppNoticeDelivery>(Json) is { } delivery)
            {
                return Result<AppNoticeDelivery>.Ok(delivery);
            }

            var code = StringOf(root, AppNoticeConstants.CodeMetadataKey);
            if (response.StatusCode == HttpStatusCode.NotFound && code == AppNoticeErrorCodeConstants.NotLinked)
                return Result<AppNoticeDelivery>.Ok(new AppNoticeDelivery { Status = AppNoticeStatusConstants.NotLinked });

            var retryAfter = response.Headers.RetryAfter?.Delta;
            return Fail(
                TypeOf(response.StatusCode),
                StringOf(root, "detail") ?? $"Identity answered {(int)response.StatusCode}.",
                code ?? "http_" + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture),
                retryAfter,
                StringOf(root, AppNoticeConstants.ReasonMetadataKey));
        }
    }

    private static async Task<JsonDocument?> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? StringOf(JsonElement? root, string name)
        => root is { ValueKind: JsonValueKind.Object } element && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static AppErrorType TypeOf(HttpStatusCode status) => status switch
    {
        HttpStatusCode.BadRequest => AppErrorType.Validation,
        HttpStatusCode.Unauthorized => AppErrorType.Unauthorized,
        HttpStatusCode.Forbidden => AppErrorType.Forbidden,
        HttpStatusCode.NotFound => AppErrorType.NotFound,
        HttpStatusCode.Conflict => AppErrorType.Conflict,
        HttpStatusCode.TooManyRequests => AppErrorType.TooManyRequests,
        HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout => AppErrorType.ExternalUnavailable,
        _ => AppErrorType.Unexpected,
    };

    private static Result<AppNoticeDelivery> Fail(AppErrorType type, string message, string code, TimeSpan? retryAfter, string? reason)
    {
        var metadata = new Dictionary<string, object?>(StringComparer.Ordinal) { [AppNoticeConstants.CodeMetadataKey] = code };
        if (retryAfter is { } wait)
            metadata[AppNoticeConstants.RetryAfterMetadataKey] = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        if (reason is not null)
            metadata[AppNoticeConstants.ReasonMetadataKey] = reason;
        return Result<AppNoticeDelivery>.Fail(AppError.Of(type, message, metadata));
    }
}
