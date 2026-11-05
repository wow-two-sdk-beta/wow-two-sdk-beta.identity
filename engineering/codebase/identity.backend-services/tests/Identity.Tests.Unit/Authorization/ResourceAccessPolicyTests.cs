using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Authorization;
using WoW.Two.Sdk.Beta.Be.Identity.Jwt;
using WoW.Two.Sdk.Beta.Be.Identity.Testing;
using WoW.Two.Sdk.Beta.Be.Identity.Tests.Jwt;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Authorization;

/// <summary>Drives the scope and audience policies over a JWT with one space-delimited claim and over a scheme with one claim per scope.</summary>
public sealed class ResourceAccessPolicyTests : IDisposable
{
    private const string Files = "https://files.example.test/";
    private const string Narrow = "https://files.example.test/admin";

    private readonly FakeTokenIssuer _issuer = new();

    public void Dispose() => _issuer.Dispose();

    [Theory]
    [InlineData("files.read", HttpStatusCode.OK)]
    [InlineData("files.read files.write", HttpStatusCode.OK)]
    [InlineData("files.write", HttpStatusCode.Forbidden)]
    [InlineData("files.reader", HttpStatusCode.Forbidden)]
    [InlineData("FILES.READ", HttpStatusCode.Forbidden)]
    public async Task RequireScope_ShouldAnswer_ForAJwtScopeClaim(string scopes, HttpStatusCode expected)
    {
        await using var host = await StartJwtHostAsync();

        (await host.StatusAsync(_issuer.IssueServiceToken("worker", Files, scopes.Split(' ')), "/read")).Should().Be(expected);
    }

    [Fact]
    public async Task RequireScope_ShouldAllApply_WhenRepeated()
    {
        await using var host = await StartJwtHostAsync();

        (await host.StatusAsync(_issuer.IssueServiceToken("worker", Files, "files.read"), "/read-write")).Should().Be(HttpStatusCode.Forbidden);
        (await host.StatusAsync(_issuer.IssueServiceToken("worker", Files, "files.write"), "/read-write")).Should().Be(HttpStatusCode.Forbidden);
        (await host.StatusAsync(_issuer.IssueServiceToken("worker", Files, "files.read", "files.write"), "/read-write")).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RequireScope_ShouldAnswer200_ForAJwtScpClaim_AndAJsonArray()
    {
        var key = SignedTokens.NewKey();
        await using var host = await ResourceHost.StartAsync(services => services.AddJwtBearerAuthentication(options =>
        {
            options.Audience = Files;
            options.TrustedIssuers.Add(new TrustedIssuer { Issuer = "https://other.test/", SigningKeys = [SignedTokens.PublicOf(key)] });
        }), MapEndpoints);

        var scp = SignedTokens.Sign(key, "https://other.test/", Files, new Dictionary<string, object> { ["scp"] = "files.read" });
        var array = SignedTokens.Sign(key, "https://other.test/", Files, new Dictionary<string, object> { ["scope"] = new[] { "files.read", "files.write" } });

        (await host.StatusAsync(scp, "/read")).Should().Be(HttpStatusCode.OK);
        (await host.StatusAsync(array, "/read-write")).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RequireScope_ShouldAnswer401_WithNoToken()
    {
        await using var host = await StartJwtHostAsync();

        using var response = await host.Client.GetAsync(new Uri("/read", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(Narrow, HttpStatusCode.OK)]
    [InlineData(Files, HttpStatusCode.Forbidden)]
    public async Task RequireAudience_ShouldAnswer_ForAJwtAudience(string audience, HttpStatusCode expected)
    {
        await using var host = await StartJwtHostAsync();

        (await host.StatusAsync(_issuer.IssueServiceToken("worker", audience), "/narrow")).Should().Be(expected);
    }

    [Fact]
    public async Task RequireAudience_ShouldAnswer401_WithNoToken()
    {
        await using var host = await StartJwtHostAsync();

        using var response = await host.Client.GetAsync(new Uri("/narrow", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("files.read", "/read", HttpStatusCode.OK)]
    [InlineData("files.write", "/read", HttpStatusCode.Forbidden)]
    [InlineData("files.read,files.write", "/read-write", HttpStatusCode.OK)]
    [InlineData("files.read", "/read-write", HttpStatusCode.Forbidden)]
    public async Task RequireScope_ShouldAnswer_ForOneClaimPerScope(string scopes, string path, HttpStatusCode expected)
    {
        await using var host = await StartClaimsHostAsync();

        (await host.StatusAsync(scopes, path)).Should().Be(expected);
    }

    [Fact]
    public async Task RequireAudience_ShouldAnswer403_ForAPrincipalWithoutAud()
    {
        await using var host = await StartClaimsHostAsync();

        (await host.StatusAsync("files.read", "/narrow")).Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public void HasScope_ShouldReadEveryClaimShape_Ordinally()
    {
        Principal(("scope", "a b")).HasScope("b").Should().BeTrue();
        Principal(("scope", "a"), ("scope", "b")).HasScope("b").Should().BeTrue();
        Principal(("scp", "a  b")).HasScope("b").Should().BeTrue();
        Principal(("scope", "a b")).HasScope("B").Should().BeFalse();
        Principal(("scope", "ab")).HasScope("a").Should().BeFalse();
        Principal(("roles", "a")).HasScope("a").Should().BeFalse();
        Principal().HasScope("a").Should().BeFalse();
    }

    [Fact]
    public void HasAudience_ShouldCompareEachAudOrdinally()
    {
        Principal(("aud", "a"), ("aud", "b")).HasAudience("b").Should().BeTrue();
        Principal(("aud", "a")).HasAudience("A").Should().BeFalse();
        Principal(("aud", "a b")).HasAudience("a").Should().BeFalse();
        Principal().HasAudience("a").Should().BeFalse();
    }

    private Task<ResourceHost> StartJwtHostAsync() =>
        ResourceHost.StartAsync(services => services.AddJwtBearerAuthentication(options =>
        {
            options.Audience = Files;
            options.Audiences.Add(Narrow);
            options.TrustedIssuers.Add(_issuer.AsTrustedIssuer());
        }), MapEndpoints);

    private static Task<ResourceHost> StartClaimsHostAsync() =>
        ResourceHost.StartAsync(services => services
            .AddAuthentication(ScopeClaimsHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, ScopeClaimsHandler>(ScopeClaimsHandler.SchemeName, null), MapEndpoints);

    private static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/read", () => "ok").RequireAuthorization(policy => policy.RequireScope("files.read"));
        endpoints.MapGet("/read-write", () => "ok").RequireAuthorization(policy => policy.RequireScope("files.read").RequireScope("files.write"));
        endpoints.MapGet("/narrow", () => "ok").RequireAuthorization(policy => policy.RequireAudience(Narrow));
    }

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value)), "test"));

    /// <summary>Authenticates the bearer value as comma-separated scopes, one claim each, as an API key's principal holds them.</summary>
    private sealed class ScopeClaimsHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "ScopeClaims";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = header["Bearer ".Length..].Split(',').Select(scope => new Claim("scope", scope));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
