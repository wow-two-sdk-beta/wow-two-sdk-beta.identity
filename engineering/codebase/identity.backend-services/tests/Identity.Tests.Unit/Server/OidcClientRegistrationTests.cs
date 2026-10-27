using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.OidcServer;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Server;

/// <summary>A configured client's registration: confidential, first-party, code + PKCE + refresh only, exact URIs.</summary>
public sealed class OidcClientRegistrationTests
{
    private static OidcClientOptions Wheelhouse() => new()
    {
        ClientId = "wheelhouse",
        ClientSecret = "test-only-secret",
        RedirectUris = ["https://wheelhouse.example.test/signin-oidc"],
        PostLogoutRedirectUris = ["https://wheelhouse.example.test/signed-out"],
        Audience = "wheelhouse-api",
        PermissionNamespace = "wheelhouse",
    };

    [Fact]
    public void Descriptor_ShouldAllowOnlyTheCodeFlowWithPkceAndRefresh()
    {
        var descriptor = OidcClientSeeder.CreateDescriptor(Wheelhouse());

        descriptor.ClientType.Should().Be(ClientTypes.Confidential);
        descriptor.ConsentType.Should().Be(ConsentTypes.Implicit, "first-party apps skip consent");
        descriptor.Permissions.Should().BeEquivalentTo(
            Permissions.Endpoints.Authorization, Permissions.Endpoints.Token, Permissions.Endpoints.EndSession,
            Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.RefreshToken, Permissions.ResponseTypes.Code,
            Permissions.Scopes.Email, Permissions.Scopes.Profile);
        descriptor.Requirements.Should().Equal(Requirements.Features.ProofKeyForCodeExchange);
        descriptor.RedirectUris.Should().Equal(new Uri("https://wheelhouse.example.test/signin-oidc"));
        descriptor.PostLogoutRedirectUris.Should().Equal(new Uri("https://wheelhouse.example.test/signed-out"));
        descriptor.Properties[OidcClientProfile.AudienceProperty].GetString().Should().Be("wheelhouse-api");
        descriptor.Properties[OidcClientProfile.PermissionNamespaceProperty].GetString().Should().Be("wheelhouse");
        descriptor.Properties[OidcClientProfile.SourceProperty].GetString().Should().Be(OidcClientProfile.ConfigurationSource);
    }

    [Fact]
    public void Registration_ShouldCompareEverythingButTheSecret()
    {
        var current = OidcClientSeeder.CreateDescriptor(Wheelhouse());
        var sameButSecret = OidcClientSeeder.CreateDescriptor(Wheelhouse() with { ClientSecret = "another" });
        var movedRedirect = OidcClientSeeder.CreateDescriptor(Wheelhouse() with { RedirectUris = ["https://wheelhouse.example.test/callback"] });
        var otherAudience = OidcClientSeeder.CreateDescriptor(Wheelhouse() with { Audience = "other" });

        OidcClientSeeder.IsSameRegistration(current, sameButSecret).Should().BeTrue();
        OidcClientSeeder.IsSameRegistration(current, movedRedirect).Should().BeFalse();
        OidcClientSeeder.IsSameRegistration(current, otherAudience).Should().BeFalse();
    }

    [Fact]
    public void Options_ShouldRefusePublicClientsLooseUrisAndDuplicates()
    {
        var options = new OidcServerOptions
        {
            SignInPath = "sign-in",
            AccessTokenLifetime = TimeSpan.Zero,
            Clients =
            [
                Wheelhouse() with { ClientSecret = null },
                Wheelhouse() with { RedirectUris = ["http://wheelhouse.example.test/cb", "https://wheelhouse.example.test/cb#x", "/relative"] },
            ],
        };

        var result = new OidcServerOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(f => f.Contains("SignInPath", StringComparison.Ordinal));
        result.Failures.Should().Contain(f => f.Contains("AccessTokenLifetime must be positive", StringComparison.Ordinal));
        result.Failures.Should().Contain(f => f.Contains("registered 2 times", StringComparison.Ordinal));
        result.Failures.Should().Contain(f => f.Contains("has no ClientSecret", StringComparison.Ordinal));
        result.Failures.Where(f => f.Contains("is not an absolute HTTPS URI", StringComparison.Ordinal)).Should().HaveCount(3);
    }

    [Fact]
    public void Options_ShouldAcceptLoopbackHttpForLocalApps()
    {
        var options = new OidcServerOptions { Clients = [Wheelhouse() with { RedirectUris = ["http://localhost:5173/signin-oidc"], PostLogoutRedirectUris = ["http://127.0.0.1:5173/"] }] };

        new OidcServerOptionsValidator().Validate(null, options).Should().Be(ValidateOptionsResult.Success);
    }

    [Theory]
    [InlineData("Production", null, false)]
    [InlineData("Development", "dev", false)]
    [InlineData("Development", null, true)]
    public void Options_ShouldRequireAPinnedIssuer_OnADeployedBoot(string environment, string? deployEnvironment, bool passesWithoutIssuer)
    {
        using var services = new ServiceCollection()
            .AddSingleton<IHostEnvironment>(new TestHostEnvironment { EnvironmentName = environment })
            .AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["DEPLOY_ENVIRONMENT"] = deployEnvironment })
                .Build())
            .BuildServiceProvider();
        var validator = new OidcServerOptionsValidator(services);

        validator.Validate(null, new OidcServerOptions { Clients = [Wheelhouse()] }).Succeeded.Should().Be(passesWithoutIssuer);
        validator.Validate(null, new OidcServerOptions { Issuer = new Uri("https://id.example.test/"), Clients = [Wheelhouse()] })
            .Succeeded.Should().BeTrue();
    }
}
