using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Beta.Be.Identity.Bff;
using Identity.Tests.E2E.Oidc;

namespace Identity.Tests.E2E.Bff;

/// <summary>
/// An Ocharo-shaped BFF in memory, signing in through the real instance of <see cref="OidcHost"/> with the
/// <c>Client</c> package: the session routes, a page, and API routes behind a session, a permission and the route's
/// organization. Its OpenID Connect back-channel reaches the instance in memory.
/// </summary>
public sealed class BffHost : IAsyncDisposable
{
    /// <summary>The BFF's host name.</summary>
    public const string HostName = "app.example.test";

    /// <summary>The BFF's origin.</summary>
    public const string Origin = "https://" + HostName;

    /// <summary>The permission the instance grants the test client's owners (its namespace included).</summary>
    public const string GrantedPermission = "e2e:deploy";

    private readonly WebApplication _app;

    private BffHost(WebApplication app) => _app = app;

    /// <summary>Gets the BFF's services.</summary>
    public IServiceProvider Services => _app.Services;

    /// <summary>A handler into the BFF.</summary>
    public HttpMessageHandler CreateHandler() => _app.GetTestServer().CreateHandler();

    /// <summary>Start a BFF against <paramref name="identity"/>.</summary>
    /// <param name="identity">The instance.</param>
    /// <param name="configure">Code configuration of the BFF, applied after its settings.</param>
    /// <param name="time">The BFF's clock; null keeps the system clock.</param>
    public static async Task<BffHost> StartAsync(OidcHost identity, Action<IdentityBffOptions>? configure = null, TimeProvider? time = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Identity:Client:Authority"] = "https://localhost/",
            ["Identity:Client:ClientId"] = OidcHost.ClientId,
            ["Identity:Client:ClientSecret"] = OidcHost.ClientSecret,
        });
        if (time is not null)
            builder.Services.AddSingleton(time);
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddProblemDetails();
        builder.Services.AddIdentityBff(configure);
        builder.Services.Configure<OpenIdConnectOptions>(
            BffAuthenticationSchemeConstants.OpenIdConnect,
            options => options.BackchannelHttpHandler = identity.Host.Server.CreateHandler());

        var app = builder.Build();
        app.UseIdentityBff();
        app.MapIdentityBffEndpoints();
        app.MapGet("/desk", () => "desk").RequireAuthorization();
        var api = app.MapGroup("/api");
        api.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new { sub = user.FindFirst("sub")?.Value })).RequireAuthorization();
        // The explicit return type keeps the lambda off the RequestDelegate overload, which would drop the result.
        api.MapGet("/token", async Task<IResult> (HttpContext http) => Results.Ok(new { accessToken = await http.GetTokenAsync("access_token") })).RequireAuthorization();
        api.MapPost("/desks", () => Results.Ok()).RequirePermission(GrantedPermission);
        api.MapPost("/admin", () => Results.Ok()).RequirePermission("e2e:admin");
        api.MapGet("/orgs/{organizationId:guid}/desks", () => Results.Ok()).RequireRouteOrganization();
        await app.StartAsync();
        return new BffHost(app);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _app.DisposeAsync();
}
