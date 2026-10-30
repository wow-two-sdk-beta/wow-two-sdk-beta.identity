using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Cookies;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Cookies;

/// <summary>Tests for <see cref="EnvironmentCookieName"/>: dev and prod on one host name keep distinct sessions.</summary>
public sealed class EnvironmentCookieNameTests
{
    private const string Plain = ".wheelhouse.auth";

    [Theory]
    [InlineData(null, Plain)]
    [InlineData("", Plain)]
    [InlineData("prod", Plain)]
    [InlineData("PROD", Plain)]
    [InlineData("dev", Plain + ".dev")]
    [InlineData(" Dev ", Plain + ".dev")]
    [InlineData("dev;path=/", Plain)]
    public void For_ShouldSeparateDevFromProd_WhenTheyShareOneHostName(string? environment, string expected)
    {
        EnvironmentCookieName.For(Plain, environment).Should().Be(expected);
    }

    [Fact]
    public void For_ShouldKeepThePlainName_ForTheNamedPrimaryEnvironment()
    {
        EnvironmentCookieName.For(Plain, "live", primaryEnvironment: "Live").Should().Be(Plain);
        EnvironmentCookieName.For(Plain, "prod", primaryEnvironment: "live").Should().Be(Plain + ".prod");
    }

    [Theory]
    [InlineData(null, ".app.auth")]
    [InlineData("prod", ".app.auth")]
    [InlineData("dev", ".app.auth.dev")]
    public void AddCookieAuthentication_ShouldNameTheCookieForItsEnvironment(string? environment, string expected)
    {
        var services = new ServiceCollection();
        services.AddCookieAuthentication(o => o.Environment = environment);
        using var provider = services.BuildServiceProvider();

        var cookie = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme).Cookie;

        cookie.Name.Should().Be(expected);
    }
}
