using System.Security.Cryptography;
using AwesomeAssertions;
using Microsoft.IdentityModel.Tokens;
using WoW.Two.Sdk.Beta.Be.Identity.OidcServer;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Server;

/// <summary>A client's service grants: what its registration permits, and what the boot refuses in the registry.</summary>
public sealed class OidcServiceAccessRegistrationTests
{
    private const string Desk = "https://desk.example.test/";
    private const string Files = "https://files.example.test/";

    private static OidcResourceOptions[] Resources() =>
    [
        new() { Name = "desk", Uri = Desk, Scopes = ["desk.media", "desk.orders"] },
        new() { Name = "files", Uri = Files, Scopes = ["files.read"], PermissionNamespace = "files" },
    ];

    private static OidcClientOptions Worker() => new()
    {
        ClientId = "worker",
        ClientSecret = "test-only-secret",
        Grants = [OidcClientGrantConstants.ClientCredentials],
        Resources = ["desk"],
        Scopes = ["desk.media"],
    };

    private static OidcClientOptions Editorial() => new()
    {
        ClientId = "editorial",
        ClientSecret = "test-only-secret",
        RedirectUris = ["https://editorial.example.test/signin-oidc"],
        Grants = [OidcClientGrantConstants.AuthorizationCode, OidcClientGrantConstants.ClientCredentials, OidcClientGrantConstants.TokenExchange],
        Resources = ["desk"],
        ExchangeResources = ["files"],
        Scopes = ["desk.media", "files.read"],
    };

    private static string PublicKeySet(bool includePrivate = false)
    {
        using var rsa = RSA.Create(2048);
        var key = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(rsa.ExportParameters(includePrivate)));
        key.Kid = "key-1";
        return KeySetOf(key);
    }

    private static string KeySetOf(JsonWebKey key)
        => $$"""{ "keys": [ {{TextOf(key)}} ] }""";

    private static string TextOf(JsonWebKey key)
        => key.HasPrivateKey
            ? $$"""{ "kty": "RSA", "kid": "{{key.Kid}}", "n": "{{key.N}}", "e": "{{key.E}}", "d": "{{key.D}}", "p": "{{key.P}}", "q": "{{key.Q}}", "dp": "{{key.DP}}", "dq": "{{key.DQ}}", "qi": "{{key.QI}}" }"""
            : $$"""{ "kty": "RSA", "kid": "{{key.Kid}}", "n": "{{key.N}}", "e": "{{key.E}}" }""";

    private static IEnumerable<string> Problems(OidcClientOptions? client = null, OidcResourceOptions? resource = null, TimeSpan? lifetime = null)
    {
        var options = new OidcServerOptions
        {
            Issuer = new Uri("https://identity.example.test/"),
            Clients = client is null ? [] : [client],
            Resources = resource is null ? [.. Resources()] : [.. Resources(), resource],
        };
        if (lifetime is { } value)
            options.ServiceTokenLifetime = value;

        return new OidcServerOptionsValidator().Validate(null, options).Failures ?? [];
    }

    [Fact]
    public void Descriptor_ShouldPermitOnlyTheTokenEndpointAndItsGrant_ForAServiceOnlyClient()
    {
        var descriptor = OidcClientSeeder.CreateDescriptor(Worker(), Resources());

        descriptor.Permissions.Should().BeEquivalentTo(
            Permissions.Endpoints.Token, Permissions.GrantTypes.ClientCredentials, Permissions.Prefixes.Resource + Desk, Permissions.Prefixes.Scope + "desk.media");
        descriptor.Requirements.Should().BeEmpty();
        descriptor.RedirectUris.Should().BeEmpty();
    }

    [Fact]
    public void Descriptor_ShouldAddServiceGrantsBesideTheCodeFlow_AndKeepEachResourceToItsGrant()
    {
        var descriptor = OidcClientSeeder.CreateDescriptor(Editorial(), Resources());

        descriptor.Permissions.Should().Contain(
        [
            Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.ClientCredentials, Permissions.GrantTypes.TokenExchange,
            Permissions.Prefixes.Resource + Desk, Permissions.Prefixes.Resource + Files,
        ]);
        descriptor.Requirements.Should().Equal(Requirements.Features.ProofKeyForCodeExchange);
        var access = descriptor.Properties[OidcClientProfile.ServiceAccessProperty];
        access.GetProperty(nameof(OidcClientServiceAccess.CredentialResources)).EnumerateArray().Select(uri => uri.GetString()).Should().Equal(Desk);
        access.GetProperty(nameof(OidcClientServiceAccess.ExchangeResources)).EnumerateArray().Select(uri => uri.GetString()).Should().Equal(Files);
    }

    [Fact]
    public void Registration_ShouldChange_WhenAResourceUriMoves()
    {
        OidcResourceOptions[] moved = [new() { Name = "desk", Uri = "https://desk2.example.test/", Scopes = ["desk.media"] }];

        OidcClientSeeder.IsSameRegistration(OidcClientSeeder.CreateDescriptor(Worker(), Resources()), OidcClientSeeder.CreateDescriptor(Worker(), moved)).Should().BeFalse();
    }

    [Fact]
    public void Options_ShouldAcceptAServiceClientWithNoRedirectUri()
    {
        Problems(Worker()).Should().BeEmpty();
        Problems(Editorial()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://files.example.test")]
    [InlineData("http://files.example.test/")]
    [InlineData("https://files.example.test/#part")]
    [InlineData("files")]
    public void Options_ShouldRefuseAResourceUriNotWrittenCanonically(string uri)
        => Problems(resource: new OidcResourceOptions { Name = "other", Uri = uri, Scopes = ["other.read"] }).Should().ContainSingle();

    [Theory]
    [InlineData("read")]
    [InlineData("files.")]
    [InlineData("Files.Read")]
    [InlineData("files:read")]
    public void Options_ShouldRefuseAScopeThatIsNotServiceDotAction(string scope)
        => Problems(resource: new OidcResourceOptions { Name = "other", Uri = "https://other.example.test/", Scopes = [scope] }).Should().ContainSingle();

    [Fact]
    public void Options_ShouldRefuseADuplicateNameOrUri()
    {
        Problems(resource: new OidcResourceOptions { Name = "desk", Uri = "https://other.example.test/", Scopes = ["other.read"] }).Should().ContainSingle();
        Problems(resource: new OidcResourceOptions { Name = "other", Uri = Desk, Scopes = ["other.read"] }).Should().ContainSingle();
    }

    [Fact]
    public void Options_ShouldKeepServiceTokensShort()
    {
        Problems(lifetime: TimeSpan.FromMinutes(10)).Should().BeEmpty();
        Problems(lifetime: TimeSpan.FromMinutes(11)).Should().ContainSingle();
        Problems(lifetime: TimeSpan.Zero).Should().ContainSingle();
    }

    [Fact]
    public void Descriptor_ShouldHoldTheKeySetAndNoSecret_ForAClientOnPrivateKeyJwt()
    {
        var descriptor = OidcClientSeeder.CreateDescriptor(Worker() with { ClientSecret = null, JsonWebKeySet = PublicKeySet() }, Resources());

        descriptor.ClientSecret.Should().BeNull();
        descriptor.ClientType.Should().Be(ClientTypes.Confidential);
        descriptor.JsonWebKeySet!.Keys.Should().ContainSingle().Which.Kty.Should().Be(JsonWebAlgorithmsKeyTypes.RSA);
    }

    [Fact]
    public void Registration_ShouldChange_WhenTheKeySetChanges()
    {
        var current = OidcClientSeeder.CreateDescriptor(Worker() with { ClientSecret = null, JsonWebKeySet = PublicKeySet() }, Resources());
        var rotated = OidcClientSeeder.CreateDescriptor(Worker() with { ClientSecret = null, JsonWebKeySet = PublicKeySet() }, Resources());
        var key = current.JsonWebKeySet!.Keys.Single();
        var same = OidcClientSeeder.CreateDescriptor(Worker() with { ClientSecret = null, JsonWebKeySet = KeySetOf(key) }, Resources());

        OidcClientSeeder.IsSameRegistration(current, rotated).Should().BeFalse();
        OidcClientSeeder.IsSameRegistration(current, same).Should().BeTrue();
    }

    [Fact]
    public void Options_ShouldTakeExactlyOneWayToAuthenticate_AndOnlyPublicKeys()
    {
        Problems(Worker() with { ClientSecret = null, JsonWebKeySet = PublicKeySet() }).Should().BeEmpty();
        Problems(Worker() with { JsonWebKeySet = PublicKeySet() }).Should().Contain(problem => problem.Contains("both"));
        Problems(Worker() with { ClientSecret = null }).Should().Contain(problem => problem.Contains("no ClientSecret or JsonWebKeySet"));
        Problems(Worker() with { ClientSecret = null, JsonWebKeySet = "not json" }).Should().Contain(problem => problem.Contains("does not parse"));
        Problems(Worker() with { ClientSecret = null, JsonWebKeySet = """{ "keys": [] }""" }).Should().Contain(problem => problem.Contains("holds no key"));
        Problems(Worker() with { ClientSecret = null, JsonWebKeySet = PublicKeySet(includePrivate: true) }).Should().Contain(problem => problem.Contains("private key"));
    }

    [Fact]
    public void Options_ShouldRefuseAClientWhoseGrantsResourcesAndScopesDisagree()
    {
        Problems(Worker() with { Grants = ["password"] }).Should().Contain(problem => problem.Contains("unknown grant"));
        Problems(Worker() with { Resources = [] }).Should().Contain(problem => problem.Contains(OidcClientGrantConstants.ClientCredentials));
        Problems(Worker() with { ExchangeResources = ["files"] }).Should().Contain(problem => problem.Contains(OidcClientGrantConstants.TokenExchange));
        Problems(Worker() with { Resources = ["nowhere"] }).Should().Contain(problem => problem.Contains("not registered"));
        Problems(Worker() with { Scopes = ["files.read"] }).Should().Contain(problem => problem.Contains("understands"));
        Problems(Worker() with { RedirectUris = ["https://worker.example.test/cb"] }).Should().Contain(problem => problem.Contains("redirect"));
        Problems(Editorial() with { Grants = [OidcClientGrantConstants.AuthorizationCode, OidcClientGrantConstants.TokenExchange], Resources = [], OrganizationId = Guid.NewGuid() })
            .Should().Contain(problem => problem.Contains("bound to an organization"));
    }
}
