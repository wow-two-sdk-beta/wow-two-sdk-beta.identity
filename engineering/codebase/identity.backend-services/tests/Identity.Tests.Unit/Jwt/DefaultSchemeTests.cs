using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Identity.Bff;
using WoW.Two.Sdk.Beta.Be.Identity.Cookies;
using WoW.Two.Sdk.Beta.Be.Identity.Jwt;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Jwt;

/// <summary>Bearer is the default scheme only on a host that names none, so a BFF's session survives adding token validation in either order.</summary>
public sealed class DefaultSchemeTests
{
    private const string Key = "a-default-scheme-test-key-of-32-bytes-at-least";

    private static void AddBearer(IServiceCollection services) => services.AddJwtBearerAuthentication(options =>
    {
        options.Issuer = "https://id.example.test/";
        options.Audience = "https://api.example.test/";
        options.SymmetricKey = Key;
    });

    private static void AddTrustedBearer(IServiceCollection services) => services.AddJwtBearerAuthentication(options =>
    {
        options.Audience = "https://api.example.test/";
        options.TrustedIssuers.Add(new TrustedIssuer { Issuer = "https://id.example.test/" });
    });

    private static async Task<string?> DefaultSchemeOfAsync(params Action<IServiceCollection>[] registrations)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        foreach (var registration in registrations)
            registration(services);

        await using var provider = services.BuildServiceProvider();
        return (await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetDefaultAuthenticateSchemeAsync())?.Name;
    }

    [Fact]
    public async Task HostWithOnlyTokens_ShouldDefaultToBearer()
    {
        (await DefaultSchemeOfAsync(AddBearer)).Should().Be(JwtBearerDefaults.AuthenticationScheme);
        (await DefaultSchemeOfAsync(AddTrustedBearer)).Should().Be(JwtBearerDefaults.AuthenticationScheme);
    }

    [Fact]
    public async Task BffHost_ShouldKeepItsCookie_InEitherOrder()
    {
        void Bff(IServiceCollection services) => services.AddIdentityBff(options =>
        {
            options.Authority = "https://id.example.test/";
            options.ClientId = "app";
            options.ClientSecret = "test-only-secret";
        });

        (await DefaultSchemeOfAsync(Bff, AddBearer)).Should().Be(BffAuthenticationSchemeConstants.Cookie);
        (await DefaultSchemeOfAsync(AddBearer, Bff)).Should().Be(BffAuthenticationSchemeConstants.Cookie);
        (await DefaultSchemeOfAsync(Bff, AddTrustedBearer)).Should().Be(BffAuthenticationSchemeConstants.Cookie);
    }

    [Fact]
    public async Task CookieHost_ShouldKeepItsCookie_InEitherOrder()
    {
        static void Cookie(IServiceCollection services) => services.AddCookieAuthentication();

        (await DefaultSchemeOfAsync(Cookie, AddBearer)).Should().Be(CookieAuthenticationDefaults.AuthenticationScheme);
        (await DefaultSchemeOfAsync(AddBearer, Cookie)).Should().Be(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    [Fact]
    public async Task HostNamingBearerItself_ShouldKeepThatChoice()
    {
        static void Named(IServiceCollection services) => services.Configure<AuthenticationOptions>(options => options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme);
        static void Cookie(IServiceCollection services) => services.AddCookieAuthentication();

        (await DefaultSchemeOfAsync(Cookie, AddBearer, Named)).Should().Be(JwtBearerDefaults.AuthenticationScheme);
    }
}
