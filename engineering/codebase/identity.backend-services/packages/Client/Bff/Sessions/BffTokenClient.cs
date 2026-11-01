using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Sessions;

/// <summary>
/// Provides the refresh grant against the instance's token endpoint, over the OpenID Connect handler's discovery and
/// back-channel, and validates the new ID token as the handler validated the first one: signature against the JWKS,
/// issuer, audience and lifetime.
/// </summary>
/// <param name="handlerOptions">The OpenID Connect handler's options.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class BffTokenClient(IOptionsMonitor<OpenIdConnectOptions> handlerOptions, ILogger<BffTokenClient> logger)
{
    /// <summary>Holds an empty user-info document, so the claim actions only delete the protocol claims.</summary>
    private static readonly JsonElement EmptyUserInfo = JsonDocument.Parse("{}").RootElement;

    /// <summary>Redeem <paramref name="refreshToken"/> for new tokens.</summary>
    /// <param name="refreshToken">The session's refresh token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<BffTokenRefreshResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(refreshToken);
        var options = handlerOptions.Get(BffAuthenticationSchemeConstants.OpenIdConnect);
        OpenIdConnectConfiguration configuration;
        HttpResponseMessage response;
        try
        {
            configuration = await options.ConfigurationManager!.GetConfigurationAsync(cancellationToken);
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = options.ClientId!,
            };
            if (!string.IsNullOrEmpty(options.ClientSecret))
                form["client_secret"] = options.ClientSecret;

            using var request = new HttpRequestMessage(HttpMethod.Post, configuration.TokenEndpoint) { Content = new FormUrlEncodedContent(form) };
            response = await options.Backchannel.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or IOException
                                              || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogUnavailable(logger, exception.GetType().Name);
            return BffTokenRefreshResult.Unavailable;
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // 4xx is the instance's verdict on the grant; 5xx and the rest are its trouble, not the session's.
                var status = (int)response.StatusCode;
                if (status is >= 400 and < 500)
                {
                    LogRejected(logger, status, ErrorOf(body));
                    return BffTokenRefreshResult.Rejected;
                }

                LogUnavailable(logger, status.ToString(CultureInfo.InvariantCulture));
                return BffTokenRefreshResult.Unavailable;
            }

            return await ReadAsync(body, options, configuration);
        }
    }

    private async Task<BffTokenRefreshResult> ReadAsync(string body, OpenIdConnectOptions options, OpenIdConnectConfiguration configuration)
    {
        string? accessToken, newRefreshToken, identityToken;
        TimeSpan? lifetime;
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            accessToken = StringOf(root, "access_token");
            newRefreshToken = StringOf(root, "refresh_token");
            identityToken = StringOf(root, "id_token");
            lifetime = root.TryGetProperty("expires_in", out var expiresIn) && expiresIn.TryGetInt64(out var seconds) ? TimeSpan.FromSeconds(seconds) : null;
        }
        catch (JsonException)
        {
            LogUnavailable(logger, "unreadable token response");
            return BffTokenRefreshResult.Unavailable;
        }

        if (string.IsNullOrEmpty(accessToken))
        {
            LogUnavailable(logger, "token response without an access token");
            return BffTokenRefreshResult.Unavailable;
        }

        IReadOnlyList<BffSessionClaim>? claims = null;
        if (identityToken is not null)
        {
            claims = await ValidateAsync(identityToken, options, configuration);
            if (claims is null)
                return BffTokenRefreshResult.Rejected;
        }

        return new BffTokenRefreshResult
        {
            Status = BffTokenRefreshStatus.Refreshed,
            AccessToken = accessToken,
            AccessTokenLifetime = lifetime,
            RefreshToken = newRefreshToken,
            IdentityToken = identityToken,
            Claims = claims,
        };
    }

    /// <summary>The person's claims in a refreshed ID token, or null when it fails validation.</summary>
    private async Task<IReadOnlyList<BffSessionClaim>?> ValidateAsync(string identityToken, OpenIdConnectOptions options, OpenIdConnectConfiguration configuration)
    {
        var parameters = options.TokenValidationParameters.Clone();
        if (!string.IsNullOrEmpty(configuration.Issuer))
            parameters.ValidIssuers = [.. parameters.ValidIssuers ?? [], configuration.Issuer];
        parameters.IssuerSigningKeys = [.. parameters.IssuerSigningKeys ?? [], .. configuration.SigningKeys];
        parameters.ValidAudience ??= options.ClientId;

        var result = await options.TokenHandler.ValidateTokenAsync(identityToken, parameters);
        if (!result.IsValid || result.ClaimsIdentity is not { } identity)
        {
            LogInvalidIdentityToken(logger, result.Exception?.GetType().Name ?? "invalid");
            return null;
        }

        // The handler's own claim actions, as it ran them at sign-in: the protocol claims (nonce, aud, exp, …) go.
        foreach (var action in options.ClaimActions)
            action.Run(EmptyUserInfo, identity, options.ClaimsIssuer ?? BffAuthenticationSchemeConstants.OpenIdConnect);

        return [.. identity.Claims.Select(ToSessionClaim)];
    }

    /// <summary>The stored form of <paramref name="claim"/>.</summary>
    /// <param name="claim">A claim of the signed-in person.</param>
    public static BffSessionClaim ToSessionClaim(Claim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        return new BffSessionClaim { Type = claim.Type, Value = claim.Value, ValueType = claim.ValueType };
    }

    private static string? StringOf(JsonElement root, string property)
        => root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string ErrorOf(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return StringOf(document.RootElement, "error") ?? "unknown";
        }
        catch (JsonException)
        {
            return "unknown";
        }
    }

    [LoggerMessage(EventId = 7411, Level = LogLevel.Information, Message = "BFF session refresh refused by the instance ({Status} {Error}); the session ends")]
    private static partial void LogRejected(ILogger logger, int status, string error);

    [LoggerMessage(EventId = 7412, Level = LogLevel.Warning, Message = "BFF session refresh could not reach the instance ({Reason}); the session keeps its tokens")]
    private static partial void LogUnavailable(ILogger logger, string reason);

    [LoggerMessage(EventId = 7413, Level = LogLevel.Warning, Message = "BFF session refresh returned an ID token that failed validation ({Reason}); the session ends")]
    private static partial void LogInvalidIdentityToken(ILogger logger, string reason);
}
