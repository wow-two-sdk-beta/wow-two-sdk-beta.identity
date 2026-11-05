using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Identity.Signals;

namespace WoW.Two.Sdk.Beta.Be.Identity.Authorization;

/// <summary>
/// Requires Identity to confirm the request's bearer token right now. It handles itself, so a host registers no handler for a
/// policy it doesn't use. No readable bearer token, an inactive one and an unreachable endpoint all fail closed.
/// </summary>
internal sealed class LiveTokenRequirement : AuthorizationHandler<LiveTokenRequirement>, IAuthorizationRequirement
{
    /// <summary>The one instance a policy needs: it holds nothing.</summary>
    public static readonly LiveTokenRequirement Instance = new();

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException"><c>AddLiveTokenCheck</c> was not called.</exception>
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, LiveTokenRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Resource is HttpContext http && await ReadBearerTokenAsync(http) is { Length: > 0 } token)
        {
            var client = http.RequestServices.GetService<LiveTokenClient>()
                ?? throw new InvalidOperationException("RequireLiveToken needs AddLiveTokenCheck.");
            if (await client.IsActiveAsync(token, http.RequestAborted))
            {
                context.Succeed(requirement);
                return;
            }
        }

        context.Fail(new AuthorizationFailureReason(this, "Identity did not confirm the token."));
    }

    /// <summary>The token the bearer scheme kept for this request (its <c>SaveToken</c>); null with no bearer scheme, such as on a cookie-only host.</summary>
    private static async Task<string?> ReadBearerTokenAsync(HttpContext http)
    {
        var schemes = http.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
        return await schemes.GetSchemeAsync(JwtBearerDefaults.AuthenticationScheme) is null
            ? null
            : await http.GetTokenAsync(JwtBearerDefaults.AuthenticationScheme, "access_token");
    }
}
