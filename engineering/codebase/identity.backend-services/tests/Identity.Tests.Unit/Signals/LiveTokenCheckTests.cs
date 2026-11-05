using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Authorization;
using WoW.Two.Sdk.Beta.Be.Identity.Jwt;
using WoW.Two.Sdk.Beta.Be.Identity.Signals;
using WoW.Two.Sdk.Beta.Be.Identity.Testing;
using WoW.Two.Sdk.Beta.Be.Identity.Tests.Jwt;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Signals;

/// <summary><c>RequireLiveToken</c> on a real pipeline, over a fake introspection endpoint: what passes, what fails closed, and what calls out.</summary>
public sealed class LiveTokenCheckTests : IDisposable
{
    private const string Files = "https://files.example.test/";
    private const string Endpoint = "https://id.example.test/connect/introspect";
    private const string Danger = "/danger";

    private readonly FakeTokenIssuer _issuer = new("https://id.example.test/");
    private readonly Introspection _introspection = new();

    public void Dispose()
    {
        _issuer.Dispose();
        _introspection.Dispose();
    }

    private string Token => _issuer.IssueServiceToken("worker", Files);

    [Fact]
    public async Task ActiveToken_ShouldPass_AsARfc7662Request_OnEveryCall()
    {
        await using var host = await StartAsync();
        var token = Token;

        (await host.StatusAsync(token, Danger)).Should().Be(HttpStatusCode.OK);
        (await host.StatusAsync(token, Danger)).Should().Be(HttpStatusCode.OK);

        _introspection.Forms.Should().HaveCount(2, "nothing is cached");
        var form = _introspection.Forms.First();
        form["token"].ToString().Should().Be(token);
        form["token_type_hint"].ToString().Should().Be("access_token");
        form["client_id"].ToString().Should().Be("files");
        form["client_secret"].ToString().Should().Be("test-only-secret");
    }

    [Theory]
    [InlineData("""{"active":false}""")]
    [InlineData("""{"active":"true"}""")]
    [InlineData("""{"sub":"person-1"}""")]
    [InlineData("""[{"active":true}]""")]
    [InlineData("not json")]
    public async Task AnythingButActiveTrue_ShouldFailClosed(string answer)
    {
        await using var host = await StartAsync();
        _introspection.Answer = answer;

        (await host.StatusAsync(Token, Danger)).Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task EndpointDown_ShouldFailClosed()
    {
        await using var host = await StartAsync();
        _introspection.IsDown = true;

        (await host.StatusAsync(Token, Danger)).Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task EndpointRefusing_ShouldFailClosed()
    {
        await using var host = await StartAsync();
        _introspection.Status = HttpStatusCode.Unauthorized;

        (await host.StatusAsync(Token, Danger)).Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OnlyThePolicysEndpoint_ShouldCallOut()
    {
        await using var host = await StartAsync();

        (await host.StatusAsync(Token)).Should().Be(HttpStatusCode.OK);
        _introspection.Forms.Should().BeEmpty("every other request stays offline");

        (await host.StatusAsync(Token, Danger)).Should().Be(HttpStatusCode.OK);
        _introspection.Forms.Should().ContainSingle();
    }

    [Fact]
    public async Task AnonymousCaller_ShouldBeChallenged_WithNoCallOut()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.GetAsync(Danger);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _introspection.Forms.Should().BeEmpty();
    }

    [Fact]
    public async Task PrivateKeyClient_ShouldAuthenticateWithAnAssertion()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportPkcs8PrivateKeyPem();
        await using var host = await StartAsync(options =>
        {
            options.ClientSecret = "";
            options.ClientPrivateKey = pem;
        });

        (await host.StatusAsync(Token, Danger)).Should().Be(HttpStatusCode.OK);

        var form = _introspection.Forms.Single();
        form.ContainsKey("client_secret").Should().BeFalse();
        form["client_assertion_type"].ToString().Should().Be("urn:ietf:params:oauth:client-assertion-type:jwt-bearer");
        form["client_assertion"].ToString().Should().NotBeEmpty();
    }

    [Fact]
    public async Task UnusedPolicy_ShouldAskNoRegistration()
    {
        await using var host = await ResourceHost.StartAsync(services =>
        {
            services.AddJwtBearerAuthentication(options =>
            {
                options.Audience = Files;
                options.TrustedIssuers.Add(_issuer.AsTrustedIssuer());
            });
            services.AddAuthorization(authorization => authorization.AddPolicy("live", policy => policy.RequireLiveToken()));
        });

        (await host.StatusAsync(Token)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Policy_ShouldWorkWithTheFakeIssuersPostConfigure()
    {
        await using var host = await StartAsync(fake: true);

        (await host.StatusAsync(Token, Danger)).Should().Be(HttpStatusCode.OK);
        _introspection.Forms.Should().ContainSingle();
    }

    [Theory]
    [InlineData("http://id.example.test/connect/introspect", "files", "secret", "HTTPS")]
    [InlineData(null, "files", "secret", "IntrospectionEndpoint")]
    [InlineData(Endpoint, "", "secret", "ClientId")]
    [InlineData(Endpoint, "files", "", "ClientSecret")]
    public void Options_ShouldBeCheckedAtBoot(string? endpoint, string clientId, string secret, string named)
    {
        var services = new ServiceCollection();
        services.AddLiveTokenCheck(options =>
        {
            options.IntrospectionEndpoint = endpoint is null ? null : new Uri(endpoint);
            options.ClientId = clientId;
            options.ClientSecret = secret;
        });
        using var provider = services.BuildServiceProvider();

        var read = () => provider.GetRequiredService<IOptions<LiveTokenCheckOptions>>().Value;

        read.Should().Throw<OptionsValidationException>().WithMessage($"*{named}*");
    }

    [Fact]
    public void Options_ShouldKeepTheSecretOutOfTheirText()
    {
        var options = new LiveTokenCheckOptions { ClientId = "files", ClientSecret = "test-only-secret", ClientPrivateKey = "test-only-key" };

        options.ToString().Should().Contain("files").And.NotContain("test-only");
    }

    private Task<ResourceHost> StartAsync(Action<LiveTokenCheckOptions>? configure = null, bool fake = false)
        => ResourceHost.StartAsync(
            services =>
            {
                services.AddJwtBearerAuthentication(options =>
                {
                    options.Audience = Files;
                    options.TrustedIssuers.Add(_issuer.AsTrustedIssuer());
                });
                if (fake)
                    services.AddFakeTokenIssuer(_issuer);

                services.AddLiveTokenCheck(options =>
                {
                    options.IntrospectionEndpoint = new Uri(Endpoint);
                    options.ClientId = "files";
                    options.ClientSecret = "test-only-secret";
                    options.BackchannelHttpHandler = _introspection;
                    configure?.Invoke(options);
                });
            },
            endpoints => endpoints.MapGet(Danger, () => "done").RequireAuthorization(policy => policy.RequireLiveToken()));

    /// <summary>Answers as <c>/connect/introspect</c> does, and keeps each form it was sent.</summary>
    private sealed class Introspection : HttpMessageHandler
    {
        public ConcurrentQueue<Dictionary<string, Microsoft.Extensions.Primitives.StringValues>> Forms { get; } = new();

        public string Answer { get; set; } = """{"active":true}""";

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public bool IsDown { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (IsDown)
                throw new HttpRequestException("The endpoint is down.");

            request.RequestUri!.ToString().Should().Be(Endpoint);
            Forms.Enqueue(QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(Status) { Content = new StringContent(Answer, Encoding.UTF8, "application/json") };
        }
    }
}
