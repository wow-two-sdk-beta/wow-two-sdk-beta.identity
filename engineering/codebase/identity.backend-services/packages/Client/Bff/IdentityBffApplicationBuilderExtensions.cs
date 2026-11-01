using Microsoft.AspNetCore.Builder;
using WoW.Two.Sdk.Beta.Be.Identity.Bff.Antiforgery;

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff;

/// <summary>Adds the BFF's request pipeline.</summary>
public static class IdentityBffApplicationBuilderExtensions
{
    /// <summary>
    /// Add authentication (the session cookie, resolved and refreshed per request), the antiforgery check of the session's
    /// unsafe API requests, and authorization, in that order; call it after routing and before the endpoints.
    /// </summary>
    /// <param name="app">The application pipeline.</param>
    public static IApplicationBuilder UseIdentityBff(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseAuthentication();
        app.UseMiddleware<BffAntiforgeryMiddleware>();
        app.UseAuthorization();
        return app;
    }
}
