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
///   - the session ends with the instance's sign-in session, whose end each token response names
///     (<c>refresh_token_expires_in</c>) and a sliding one moves; an instance that names none ends it
///     <see cref="IdentityBffOptions.SessionLifetime"/> after the sign-in
///   - it also ends when the instance refuses the refresh, or when the access token ran out with no refresh token; an
///     unreachable instance keeps it
///   - a person's changed access (<see cref="BffAccessChangeRepository"/>) also starts a refresh once, under the same gate, so the
///     next request carries fresh claims; each request reads one more cache key for it. Without a refresh token the session
///     keeps its tokens until they run out, since a role change signs nobody out
///   - a refresh and the save of its tokens run apart from the request, bounded by <see cref="RefreshTimeout"/>: the
///     instance rotates the refresh token as it answers, so a browser that leaves mid-refresh must not strand the new one
///   - a refresh undoes neither signal: it doesn't save a session that ended meanwhile, and its claims date from when it asked,
///     so a change heard meanwhile refreshes once more; ending takes the same gate, so it can't slip between the check and the save
///   - a sign-in's claims date from before its code exchange, so a change heard during it refreshes too
/// </remarks>
/// <param name="repository">The session store.</param>
/// <param name="protector">The payload protector.</param>
/// <param name="tokens">The refresh grant.</param>
/// <param name="options">The BFF options.</param>
/// <param name="time">The clock.</param>
/// <param name="logger">The logger.</param>
/// <param name="accessChanges">The people whose access changed; null never refreshes for it.</param>
internal sealed partial class BffSessionService(
    IBffSessionRepository repository,
    BffSessionProtector protector,
    BffTokenClient tokens,
    IOptions<IdentityBffOptions> options,
    TimeProvider time,
    ILogger<BffSessionService> logger,
    BffAccessChangeRepository? accessChanges = null)
{
    /// <summary>Holds the request item that marks the session ended in this request, so signing out never ends it twice.</summary>
    private const string EndedItem = "WoW.Two.Sdk.Beta.Be.Identity.Bff.SessionEnded";

    /// <summary>The sign-in property holding the instance session's end, from the code exchange's token response.</summary>
    private const string InstanceEndItem = ".bff.instance_session_end";

    /// <summary>The sign-in property holding when the code exchange began, the latest its tokens are sure to include.</summary>
    private const string ClaimsAskedItem = ".bff.claims_asked_at";

    /// <summary>How long one refresh may take, the save of its tokens excluded; past it the session keeps its tokens.</summary>
    public static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(30);

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

    /// <summary>Record in the sign-in's <paramref name="properties"/> when the instance's session ends.</summary>
    /// <param name="properties">The code flow's properties.</param>
    /// <param name="end">The instant the instance named.</param>
    public static void WriteInstanceEnd(AuthenticationProperties properties, DateTimeOffset end)
    {
        ArgumentNullException.ThrowIfNull(properties);
        properties.Items[InstanceEndItem] = end.ToString("o", CultureInfo.InvariantCulture);
    }

    /// <summary>Record in the sign-in's <paramref name="properties"/> when the code exchange began.</summary>
    /// <param name="properties">The code flow's properties.</param>
    /// <param name="at">The instant, on this host's clock.</param>
    public static void WriteClaimsAskedAt(AuthenticationProperties properties, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(properties);
        properties.Items[ClaimsAskedItem] = at.ToString("o", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? ReadInstant(AuthenticationProperties properties, string item)
        => properties.Items.TryGetValue(item, out var value)
           && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var end)
            ? end
            : null;

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
            ClaimsAsOf = ReadInstant(properties, ClaimsAskedItem) ?? now,
            ExpiresAt = ReadInstant(properties, InstanceEndItem) ?? now + options.Value.SessionLifetime,
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
        if (session is null || !await NeedsRefreshAsync(session.State, cancellationToken))
            return session;

        var gate = GateOf(reference.SessionId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Another request of this session may have refreshed while this one waited.
            session = await LoadLiveAsync(reference, cancellationToken);
            return session is null || !await NeedsRefreshAsync(session.State, cancellationToken) ? session : await RefreshAsync(session);
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

        // At once, so no request opens it while a refresh finishes.
        await repository.RemoveAsync(sessionId, cancellationToken);

        // Once more behind the gate, whatever became of the request: a refresh past its check would write the session back.
        var gate = GateOf(sessionId);
        await gate.WaitAsync(CancellationToken.None);
        try
        {
            await repository.RemoveAsync(sessionId, CancellationToken.None);
        }
        finally
        {
            gate.Release();
        }

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

    /// <summary>
    /// Refresh the session's tokens on a token of its own, never the request's: the instance spends the old refresh token
    /// as it answers, so the new one is saved even when the browser left meanwhile.
    /// </summary>
    private async Task<BffSession?> RefreshAsync(BffSession session)
    {
        var state = session.State;
        if (state.RefreshToken is not { } refreshToken)
            return await EndWithAsync(session.SessionId, "access token ran out without a refresh token", CancellationToken.None);

        // Read first: the tokens this refresh brings are from after any change already recorded.
        var changedAt = await AccessChangedAtAsync(state, CancellationToken.None);

        // The claims date from here: a change heard while the instance answers may not be in them.
        var askedAt = time.GetUtcNow();

        BffTokenRefreshResult refreshed;
        using (var timeout = new CancellationTokenSource(RefreshTimeout, time))
        {
            try
            {
                refreshed = await tokens.RefreshAsync(refreshToken, timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                LogRefreshTimedOut(logger);
                return session;
            }
        }

        switch (refreshed.Status)
        {
            case BffTokenRefreshStatus.Rejected:
                return await EndWithAsync(session.SessionId, "refresh refused", CancellationToken.None);
            case BffTokenRefreshStatus.Unavailable:
                return session;
        }

        // A refresh names the same person; anything else ends the session rather than switch who it signs in.
        var subject = state.Claims.FirstOrDefault(claim => claim.Type == JwtRegisteredClaimNames.Sub)?.Value;
        var renewedSubject = refreshed.Claims?.FirstOrDefault(claim => claim.Type == JwtRegisteredClaimNames.Sub)?.Value;
        if (refreshed.Claims is not null && !string.Equals(subject, renewedSubject, StringComparison.Ordinal))
            return await EndWithAsync(session.SessionId, "refresh named another person", CancellationToken.None);

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
                ExpiresAt = refreshed.SessionLifetime is { } remaining ? now + remaining : state.ExpiresAt,

                // A marker ahead of this host's clock, from a replica's, must not refresh every request until they meet.
                ClaimsAsOf = changedAt > askedAt ? changedAt : askedAt,
            },
        };

        // Ended while the instance answered: saving would bring it back.
        if (await repository.FindAsync(session.SessionId, CancellationToken.None) is null)
        {
            LogEnded(logger, "ended during a refresh");
            return null;
        }

        // The instance already rotated the refresh token: the save must land whatever became of the request.
        await SaveAsync(renewed, CancellationToken.None);
        LogRefreshed(logger);
        return renewed;
    }

    private SemaphoreSlim GateOf(string sessionId)
        => _gates[(uint)StringComparer.Ordinal.GetHashCode(sessionId) % (uint)_gates.Length];

    private async ValueTask<bool> NeedsRefreshAsync(BffSessionState state, CancellationToken cancellationToken)
    {
        if (state.AccessTokenExpiresAt is { } expiresAt && expiresAt - options.Value.AccessTokenRefreshMargin <= time.GetUtcNow())
            return true;

        return state.RefreshToken is not null
            && await AccessChangedAtAsync(state, cancellationToken) is { } changedAt
            && changedAt > (state.ClaimsAsOf ?? state.SignedInAt);
    }

    private async ValueTask<DateTimeOffset?> AccessChangedAtAsync(BffSessionState state, CancellationToken cancellationToken)
        => accessChanges is not null && state.Claims.FirstOrDefault(claim => claim.Type == JwtRegisteredClaimNames.Sub)?.Value is { } subject
            ? await accessChanges.FindAsync(subject, cancellationToken)
            : null;

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

    [LoggerMessage(EventId = 7404, Level = LogLevel.Warning, Message = "BFF session refresh timed out; the session keeps its tokens")]
    private static partial void LogRefreshTimedOut(ILogger logger);
}
