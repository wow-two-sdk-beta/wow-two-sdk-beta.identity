using System.Globalization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

namespace WoW.Two.Sdk.Beta.Be.Identity.Signals;

/// <summary>
/// Refuses a validated bearer token the deny list matches, so its caller gets <c>401</c>, and otherwise runs any
/// <c>OnTokenValidated</c> the host set. A store that can't answer throws, so the request fails rather than lets a token through.
/// </summary>
internal sealed class TokenDenyListBearerEvents : IPostConfigureOptions<JwtBearerOptions>
{
    /// <inheritdoc />
    public void PostConfigure(string? name, JwtBearerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (name != JwtBearerDefaults.AuthenticationScheme)
            return;

        // The handler would take its events from this type, and a refusal wrapped into Events would never run.
        if (options.EventsType is not null)
            throw new InvalidOperationException($"AddTokenDenyList needs {nameof(JwtBearerOptions)}.{nameof(JwtBearerOptions.Events)}, not {nameof(JwtBearerOptions.EventsType)}.");

        var events = options.Events ??= new JwtBearerEvents();
        var host = events.OnTokenValidated;
        events.OnTokenValidated = async context =>
        {
            if (await IsDeniedAsync(context))
            {
                context.Fail("The token was revoked.");
                return;
            }

            await host(context);
        };
    }

    private static async Task<bool> IsDeniedAsync(TokenValidatedContext context)
    {
        var issuer = Read(context, JwtRegisteredClaimNames.Iss) ?? context.SecurityToken?.Issuer;
        if (string.IsNullOrEmpty(issuer))
            return false;

        return await context.HttpContext.RequestServices.GetRequiredService<ITokenDenyList>().IsDeniedAsync(
            issuer,
            Read(context, SessionClaimTypeConstants.SessionId),
            Read(context, JwtRegisteredClaimNames.Sub),
            Read(context, OrganizationClaimTypeConstants.Id),
            IssuedAt(context),
            context.HttpContext.RequestAborted);
    }

    /// <summary>A claim as the token wrote it, whatever a host's own claim mapping made of it on the principal.</summary>
    private static string? Read(TokenValidatedContext context, string claim)
        => context.SecurityToken is JsonWebToken token && token.TryGetPayloadValue<string>(claim, out var value)
            ? value
            : context.Principal?.FindFirst(claim)?.Value;

    private static DateTimeOffset? IssuedAt(TokenValidatedContext context)
    {
        if (context.SecurityToken is JsonWebToken token)
            return token.IssuedAt == default ? null : new DateTimeOffset(token.IssuedAt, TimeSpan.Zero);

        return long.TryParse(context.Principal?.FindFirst(JwtRegisteredClaimNames.Iat)?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }
}
