using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Logging;

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Sessions;

/// <summary>
/// Keeps the end the instance names for the sign-in session (<c>refresh_token_expires_in</c>), so the BFF session ends with
/// it, and handles a failed code callback (a refused sign-in, a replayed or expired callback) by sending the browser back
/// to its return URL, signed out.
/// </summary>
/// <param name="time">The clock the end is counted from.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class BffOpenIdConnectEvents(TimeProvider time, ILogger<BffOpenIdConnectEvents> logger) : OpenIdConnectEvents
{
    /// <inheritdoc />
    public override Task TokenResponseReceived(TokenResponseReceivedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (BffTokenClient.SessionLifetimeOf(context.TokenEndpointResponse.GetParameter(BffTokenClient.RefreshTokenExpiresInParameter)) is { } lifetime)
            BffSessionService.WriteInstanceEnd(context.Properties!, time.GetUtcNow() + lifetime);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task RemoteFailure(RemoteFailureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        LogRemoteFailure(logger, context.Failure?.GetType().Name ?? "unknown", context.Failure?.Message ?? string.Empty);
        context.Response.Redirect(BffReturnUrl.Normalize(context.Properties?.RedirectUri));
        context.HandleResponse();
        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 7421, Level = LogLevel.Warning, Message = "BFF sign-in callback failed ({Failure}): {Message}")]
    private static partial void LogRemoteFailure(ILogger logger, string failure, string message);
}
