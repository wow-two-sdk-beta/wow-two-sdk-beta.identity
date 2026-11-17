using System.Globalization;
using System.Net;
using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Identity.Members;

/// <summary>
/// Integrates organization members over a running Identity instance (Forms 2 and 3):
/// <c>GET /api/identity/v1/organizations/{organizationId}/members</c> with the client-credentials token the
/// <see cref="HttpClient"/>'s handler adds (<c>AddOAuth2ClientCredentials</c>, the resource being the instance's member
/// audience, the scope <see cref="MemberConstants.Scope"/>).
/// </summary>
/// <remarks>
///   - <c>200</c> reads the body's <c>data</c> list
///   - any other answer is a failure carrying the problem's <c>identityCode</c> (or <c>http_{status}</c>); an instance that
///     cannot be reached reads <see cref="MemberConstants.UnavailableCode"/>
/// </remarks>
/// <param name="http">The client, its base address the instance's address.</param>
public sealed class IdentityServiceMemberBroker(HttpClient http) : IMemberBroker
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<MemberModel>>> ListAsync(Guid organizationId, string? role = null, CancellationToken cancellationToken = default)
    {
        var path = MemberConstants.PathOf(organizationId).TrimStart('/');
        if (!string.IsNullOrWhiteSpace(role))
            path += $"?{MemberConstants.RoleParameter}={Uri.EscapeDataString(role.Trim())}";

        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(path, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return Fail(AppErrorType.ExternalUnavailable, "The Identity instance could not be reached.", MemberConstants.UnavailableCode);
        }

        using (response)
        {
            using var body = await ReadAsync(response, cancellationToken);
            var root = body?.RootElement;
            if (response.StatusCode == HttpStatusCode.OK
                && root is { ValueKind: JsonValueKind.Object } envelope
                && envelope.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Array
                && data.Deserialize<List<MemberModel>>(Json) is { } members)
            {
                return Result<IReadOnlyList<MemberModel>>.Ok(members);
            }

            return Fail(
                TypeOf(response.StatusCode),
                StringOf(root, "detail") ?? $"Identity answered {(int)response.StatusCode}.",
                StringOf(root, MemberConstants.CodeMetadataKey) ?? "http_" + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
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
        HttpStatusCode.TooManyRequests => AppErrorType.TooManyRequests,
        HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout => AppErrorType.ExternalUnavailable,
        _ => AppErrorType.Unexpected,
    };

    private static Result<IReadOnlyList<MemberModel>> Fail(AppErrorType type, string message, string code)
        => Result<IReadOnlyList<MemberModel>>.Fail(AppError.Of(type, message, new Dictionary<string, object?>(StringComparer.Ordinal) { [MemberConstants.CodeMetadataKey] = code }));
}
