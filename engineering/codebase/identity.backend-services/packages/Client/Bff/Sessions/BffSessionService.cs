using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Sessions;

/// <summary>
/// Provides the BFF session's life: it starts one from the code flow's principal and tokens, resolves it from the
/// cookie's reference on each request, refreshes its tokens inside the absolute window, and ends it.
/// </summary>
/// <remarks>
///   - the session lives in <see cref="IBffSessionRepository"/> keyed on the instance's <c>sid</c>, encrypted by
///     <see cref="BffSessionProtector"/>; the cookie carries only the <c>sid</c> and a per-sign-in reference
///   - a refresh runs once the access token is within <see cref="IdentityBffOptions.AccessTokenRefreshMargin"/> of its
///     end; requests of one session in one process wait for one refresh rather than spend the refresh token twice
///   - the session ends at <see cref="IdentityBffOptions.SessionLifetime"/> after the sign-in, when the instance refuses
///     the refresh, or when the access token ran out with no refresh token; an unreachable instance keeps it
/// </remarks>
/// <param name="repository">The session store.</param>
/// <param name="protector">The payload protector.</param>
/// <param name="tokens">The refresh grant.</param>
/// <param name="options">The BFF options.</param>
/// <param name="time">The clock.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class BffSessionService(
    IBffSessionRepository repository,
    BffSessionProtector protector,
    BffTokenClient tokens,
    IOptions<IdentityBffOptions> options,
    TimeProvider time,
    ILogger<BffSessionService> logger)
{
    /// <summary>Holds the request item that marks the session ended in this request, so signing out never ends it twice.</summary>
    private const string EndedItem = "WoW.Two.Sdk.Beta.Be.Identity.Bff.SessionEnded";

    /// <summary>Holds the gates that serialize one session's refreshes in this process, striped by <c>sid</c>.</summary>
    private readonly SemaphoreSlim[] _gates = [.. Enumerable.Range(0, 32).Select(_ => new SemaphoreSlim(1, 1))];

    /// <summary>The principal a cookie ticket carries: authenticated, without claims; the claims stay server-side.</summary>
    public static ClaimsPrincipal ReferencePrincipal()
        => new(new ClaimsIdentity(authenticationType: BffAuthenticationSchemeConstants.Cookie));

    /// <summary>Mark the session of <paramref name="http"/> ended in this request.</summary>
    /// <param name="http">The request.</param>
    public static void MarkEnded(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        http.Items[EndedItem] = true;
    }

    /// <summary>Whether this request already ended its session.</summary>
    /// <param name="http">The request.</param>
    public static bool IsEnded(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        return http.Items.ContainsKey(EndedItem);
    }

    /// <summary>Start a session from the code flow's principal and the tokens the handler saved into <paramref name="properties"/>.</summary>
    /// <param name="principal">The person, from the validated ID token.</param>
    /// <param name="properties">The sign-in properties carrying the tokens.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<BffSession> StartAsync(ClaimsPrincipal principal, AuthenticationProperties properties, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(properties);

        var now = time.GetUtcNow();
        var sessionId = principal.FindFirst(JwtRegisteredClaimNames.Sid)?.Value is { Length: > 0 } sid ? sid : NewSecret();
        var state = new BffSessionState
        {
            Reference = NewSecret(),
            Claims = [.. principal.Claims.Select(BffTokenClient.ToSessionClaim)],
            AccessToken = properties.GetTokenValue("access_token"),
            AccessTokenExpiresAt = DateTimeOffset.TryParse(properties.GetTokenValue("expires_at"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : null,
            RefreshToken = properties.GetTokenValue("refresh_token"),
            IdentityToken = properties.GetTokenValue("id_token"),
            SignedInAt = now,
            ExpiresAt = now + options.Value.SessionLifetime,
        };

        var session = new BffSession { SessionId = sessionId, State = state };
        await SaveAsync(session, cancellationToken);
        LogStarted(logger);
        return session;
    }

    /// <summary>
    /// The live session <paramref name="reference"/> names, refreshed when its access token nears its end; null when it is
    /// gone, belongs to a later sign-in, or ended here.
    /// </summary>
    /// <param name="reference">The cookie's session reference.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<BffSession?> ResolveAsync(BffSessionReference reference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var session = await LoadLiveAsync(reference, cancellationToken);
        if (session is null || !NeedsRefresh(session.State))
            return session;

        var gate = _gates[(uint)StringComparer.Ordinal.GetHashCode(reference.SessionId) % (uint)_gates.Length];
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Another request of this session may have refreshed while this one waited.
            session = await LoadLiveAsync(reference, cancellationToken);
            return session is null || !NeedsRefresh(session.State) ? session : await RefreshAsync(session, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>End the session stored under <paramref name="sessionId"/>.</summary>
    /// <param name="sessionId">The session's <c>sid</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task EndAsync(string sessionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        await repository.RemoveAsync(sessionId, cancellationToken);
        LogEnded(logger, "signed out");
    }

    /// <summary>The person of <paramref name="session"/>, with <c>name</c> and <c>role</c> as the name and role claim types.</summary>
    /// <param name="session">The live session.</param>
    public static ClaimsPrincipal CreatePrincipal(BffSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var claims = session.State.Claims.Select(claim => new Claim(claim.Type, claim.Value, claim.ValueType));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, BffAuthenticationSchemeConstants.Cookie, JwtRegisteredClaimNames.Name, "role"));
    }

    /// <summary>The tokens of <paramref name="session"/>, as <c>GetTokenAsync</c> reads them within a request.</summary>
    /// <param name="session">The live session.</param>
    public static IEnumerable<AuthenticationToken> TokensOf(BffSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var state = session.State;
        if (state.AccessToken is { } access)
            yield return new AuthenticationToken { Name = "access_token", Value = access };
        if (state.RefreshToken is { } refresh)
            yield return new AuthenticationToken { Name = "refresh_token", Value = refresh };
        if (state.IdentityToken is { } identity)
            yield return new AuthenticationToken { Name = "id_token", Value = identity };
        if (state.AccessTokenExpiresAt is { } expiresAt)
            yield return new AuthenticationToken { Name = "expires_at", Value = expiresAt.ToString("o", CultureInfo.InvariantCulture) };
    }

    /// <summary>The stored session <paramref name="reference"/> names, while it matches the reference and lies inside its window.</summary>
    private async Task<BffSession?> LoadLiveAsync(BffSessionReference reference, CancellationToken cancellationToken)
    {
        var record = await repository.FindAsync(reference.SessionId, cancellationToken);
        var state = record is null ? null : protector.Unprotect(record.Payload);
        if (state is null)
            return null;

        // A later sign-in under the same sid drew a new reference: the older cookie no longer opens the session.
        if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(state.Reference), System.Text.Encoding.UTF8.GetBytes(reference.Reference)))
            return null;

        if (time.GetUtcNow() >= state.ExpiresAt)
            return await EndWithAsync(reference.SessionId, "past its absolute window", cancellationToken);

        return new BffSession { SessionId = reference.SessionId, State = state };
    }

    private async Task<BffSession?> RefreshAsync(BffSession session, CancellationToken cancellationToken)
    {
        var state = session.State;
        if (state.RefreshToken is not { } refreshToken)
            return await EndWithAsync(session.SessionId, "access token ran out without a refresh token", cancellationToken);

        var refreshed = await tokens.RefreshAsync(refreshToken, cancellationToken);
        switch (refreshed.Status)
        {
            case BffTokenRefreshStatus.Rejected:
                return await EndWithAsync(session.SessionId, "refresh refused", cancellationToken);
            case BffTokenRefreshStatus.Unavailable:
                return session;
        }

        // A refresh names the same person; anything else ends the session rather than switch who it signs in.
        var subject = state.Claims.FirstOrDefault(claim => claim.Type == JwtRegisteredClaimNames.Sub)?.Value;
        var renewedSubject = refreshed.Claims?.FirstOrDefault(claim => claim.Type == JwtRegisteredClaimNames.Sub)?.Value;
        if (refreshed.Claims is not null && !string.Equals(subject, renewedSubject, StringComparison.Ordinal))
            return await EndWithAsync(session.SessionId, "refresh named another person", cancellationToken);

        var now = time.GetUtcNow();
        var renewed = session with
        {
            State = state with
            {
                Claims = refreshed.Claims ?? state.Claims,
                AccessToken = refreshed.AccessToken,
                AccessTokenExpiresAt = refreshed.AccessTokenLifetime is { } lifetime ? now + lifetime : null,
                RefreshToken = refreshed.RefreshToken ?? refreshToken,
                IdentityToken = refreshed.IdentityToken ?? state.IdentityToken,
            },
        };
        await SaveAsync(renewed, cancellationToken);
        LogRefreshed(logger);
        return renewed;
    }

    private bool NeedsRefresh(BffSessionState state)
        => state.AccessTokenExpiresAt is { } expiresAt && expiresAt - options.Value.AccessTokenRefreshMargin <= time.GetUtcNow();

    private async Task<BffSession?> EndWithAsync(string sessionId, string reason, CancellationToken cancellationToken)
    {
        await repository.RemoveAsync(sessionId, cancellationToken);
        LogEnded(logger, reason);
        return null;
    }

    private Task SaveAsync(BffSession session, CancellationToken cancellationToken)
        => repository.SaveAsync(new BffSessionRecord
        {
            SessionId = session.SessionId,
            ExpiresAt = session.State.ExpiresAt,
            Payload = protector.Protect(session.State),
        }, cancellationToken);

    private static string NewSecret() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    [LoggerMessage(EventId = 7401, Level = LogLevel.Information, Message = "BFF session started")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(EventId = 7402, Level = LogLevel.Debug, Message = "BFF session refreshed its tokens")]
    private static partial void LogRefreshed(ILogger logger);

    [LoggerMessage(EventId = 7403, Level = LogLevel.Information, Message = "BFF session ended: {Reason}")]
    private static partial void LogEnded(ILogger logger, string reason);
}
