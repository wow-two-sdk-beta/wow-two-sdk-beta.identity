using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Jwt;

/// <summary>A resource server on a real <see cref="TestServer"/>: the pipeline, the policies and the registration under test.</summary>
internal sealed class ResourceHost(WebApplication app) : IAsyncDisposable
{
    public HttpClient Client { get; } = app.GetTestClient();

    /// <summary>Starts a host with <c>/claims</c>, open to any authenticated caller, and the endpoints <paramref name="map"/> adds.</summary>
    /// <param name="services">Registers the authentication under test.</param>
    /// <param name="map">Adds more endpoints.</param>
    public static async Task<ResourceHost> StartAsync(Action<IServiceCollection> services, Action<IEndpointRouteBuilder>? map = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddAuthorization();
        services(builder.Services);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/claims", (ClaimsPrincipal user) => string.Join('\n', user.Claims.Select(claim => $"{claim.Type}={claim.Value}")))
            .RequireAuthorization();
        map?.Invoke(app);
        await app.StartAsync();
        return new ResourceHost(app);
    }

    public async Task<HttpStatusCode> StatusAsync(string token, string path = "/claims")
    {
        using var response = await SendAsync(token, path);
        return response.StatusCode;
    }

    /// <summary>Reads what the bearer's principal holds, one <c>type=value</c> pair per claim, or fails on a refused token.</summary>
    /// <param name="token">The bearer token.</param>
    public async Task<ILookup<string, string>> ClaimsAsync(string token)
    {
        using var response = await SendAsync(token, "/claims");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadAsStringAsync())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .ToLookup(line => line[..line.IndexOf('=', StringComparison.Ordinal)], line => line[(line.IndexOf('=', StringComparison.Ordinal) + 1)..], StringComparer.Ordinal);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await app.DisposeAsync();
    }

    private async Task<HttpResponseMessage> SendAsync(string token, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Client.SendAsync(request);
    }
}
