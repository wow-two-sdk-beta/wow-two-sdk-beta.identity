using System.Text;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using WoW.Two.Sdk.Beta.Be.Identity.Jwt;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Jwt;

/// <summary>Verifies what the boot refuses and what trusted-issuer mode pins.</summary>
public sealed class TrustedIssuerRegistrationTests
{
    private const string Issuer = "https://issuer.example.test/";
    private const string Files = "https://files.example.test/";

    private static readonly TrustedIssuer Listed = new() { Issuer = Issuer };

    [Theory]
    [InlineData("issuer")]
    [InlineData("symmetric")]
    [InlineData("metadata")]
    public void Boot_ShouldRefuseOneIssuerSettings_BesideTrustedIssuers(string setting)
    {
        var register = () => Register(options =>
        {
            options.Audience = Files;
            options.TrustedIssuers.Add(Listed);
            Set(options, setting);
        });

        register.Should().Throw<InvalidOperationException>().WithMessage("*Issuer, SymmetricKey and MetadataAddress*");
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("symmetric")]
    [InlineData("metadata")]
    public void Boot_ShouldRefuseOneIssuerSettings_BesideAResolver(string setting)
    {
        var register = () => Register(options =>
        {
            options.Audience = Files;
            options.UseTrustedIssuerResolver<StoreIssuerResolver>();
            Set(options, setting);
        });

        register.Should().Throw<InvalidOperationException>().WithMessage("*Issuer, SymmetricKey and MetadataAddress*");
    }

    [Fact]
    public void Boot_ShouldRequireAnAudience_HereOrOnEveryListedIssuer()
    {
        var none = () => Register(options => options.TrustedIssuers.Add(Listed));
        none.Should().Throw<InvalidOperationException>().WithMessage("*need an audience*");

        var onOneOfTwo = () => Register(options =>
        {
            options.TrustedIssuers.Add(Listed with { Audiences = [Files] });
            options.TrustedIssuers.Add(new TrustedIssuer { Issuer = "https://other.example.test/" });
        });
        onOneOfTwo.Should().Throw<InvalidOperationException>().WithMessage("*need an audience*");

        var resolverAlone = () => Register(options => options.UseTrustedIssuerResolver<StoreIssuerResolver>());
        resolverAlone.Should().Throw<InvalidOperationException>().WithMessage("*need an audience*");

        var onEvery = () => Register(options => options.TrustedIssuers.Add(Listed with { Audiences = [Files] }));
        onEvery.Should().NotThrow();

        var inAudiences = () => Register(options =>
        {
            options.Audiences.Add(Files);
            options.UseTrustedIssuerResolver<StoreIssuerResolver>();
        });
        inAudiences.Should().NotThrow();
    }

    [Fact]
    public void Boot_ShouldRefuseABadListedIssuer()
    {
        Refuses(Listed with { Issuer = " " }, "*Issuer is required*");
        Refuses(Listed with { Audiences = [" "] }, "*empty audience*");
        Refuses(Listed with { SigningKeys = [SignedTokens.NewKey()], MetadataAddress = new Uri(Issuer) }, "*both SigningKeys and MetadataAddress*");
        Refuses(Listed with { SigningKeys = [] }, "*empty SigningKeys*");
        Refuses(Listed with { SigningKeys = [new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('k', 32)))] }, "*symmetric key*");
        Refuses(Listed with { MetadataAddress = new Uri("/discovery", UriKind.Relative) }, "*absolute URI*");
        Refuses(Listed with { MetadataAddress = new Uri("http://issuer.example.test/discovery") }, "*must use HTTPS*");
        Refuses(new TrustedIssuer { Issuer = "http://issuer.example.test/" }, "*must use HTTPS*");
        Refuses(new TrustedIssuer { Issuer = "not a url" }, "*not a URL*");
    }

    [Fact]
    public void Boot_ShouldRefuseTheSameIssuerTwice()
    {
        var register = () => Register(options =>
        {
            options.Audience = Files;
            options.TrustedIssuers.AddRange([Listed, Listed]);
        });

        register.Should().Throw<InvalidOperationException>().WithMessage("*twice*");
    }

    [Fact]
    public void Boot_ShouldRefuseAnEmptyAudience_InEitherMode()
    {
        var trusted = () => Register(options =>
        {
            options.Audiences.Add(" ");
            options.TrustedIssuers.Add(Listed);
        });
        trusted.Should().Throw<InvalidOperationException>().WithMessage("*empty audience*");

        var single = () => Register(options =>
        {
            options.Issuer = Issuer;
            options.Audience = Files;
            options.SymmetricKey = new string('k', 32);
            options.Audiences.Add("");
        });
        single.Should().Throw<InvalidOperationException>().WithMessage("*empty audience*");
    }

    [Fact]
    public void TrustedMode_ShouldPinTheAsymmetricFamily_WhateverAlgorithmSays()
    {
        using var provider = Register(options =>
        {
            options.Audience = Files;
            options.Algorithm = SecurityAlgorithms.HmacSha256;
            options.TrustedIssuers.Add(Listed);
        });

        var bearer = BearerOf(provider);
        bearer.TokenValidationParameters.ValidAlgorithms.Should().NotContain(algorithm => algorithm.StartsWith("HS", StringComparison.Ordinal));
        bearer.TokenValidationParameters.ValidAlgorithms.Should().Contain([SecurityAlgorithms.RsaSha256, SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.RsaSsaPssSha256]);
    }

    [Fact]
    public void TrustedMode_ShouldReplaceTheTokenHandlers_AndKeepClaimsAndTheToken()
    {
        using var provider = Register(options =>
        {
            options.Audience = Files;
            options.TrustedIssuers.Add(Listed);
        });

        var bearer = BearerOf(provider);
        bearer.TokenHandlers.Should().ContainSingle().Which.Should().BeOfType<TrustedIssuerTokenHandler>();
        bearer.MapInboundClaims.Should().BeFalse();
        bearer.SaveToken.Should().BeTrue();
        bearer.MetadataAddress.Should().BeNullOrEmpty();
    }

    [Fact]
    public void SingleIssuer_ShouldKeepItsOwnHandlers()
    {
        using var provider = Register(options =>
        {
            options.Issuer = Issuer;
            options.Audience = Files;
            options.SymmetricKey = new string('k', 32);
        });

        var bearer = BearerOf(provider);
        bearer.TokenHandlers.Should().NotContain(handler => handler is TrustedIssuerTokenHandler);
        bearer.TokenValidationParameters.ValidAudience.Should().Be(Files);
        bearer.TokenValidationParameters.ValidAudiences.Should().BeNull();
    }

    private static void Refuses(TrustedIssuer issuer, string message)
    {
        var register = () => Register(options =>
        {
            options.Audience = Files;
            options.TrustedIssuers.Add(issuer);
        });
        register.Should().Throw<InvalidOperationException>().WithMessage(message);
    }

    private static void Set(JwtOptions options, string setting)
    {
        if (setting == "issuer")
            options.Issuer = Issuer;
        else if (setting == "symmetric")
            options.SymmetricKey = new string('k', 32);
        else
            options.MetadataAddress = new Uri("https://issuer.example.test/.well-known/openid-configuration");
    }

    private static JwtBearerOptions BearerOf(ServiceProvider provider) =>
        provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

    private static ServiceProvider Register(Action<JwtOptions> configure) =>
        new ServiceCollection().AddJwtBearerAuthentication(configure).BuildServiceProvider();
}
