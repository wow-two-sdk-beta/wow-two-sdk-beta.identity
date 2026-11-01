using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Bff;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Bff;

/// <summary>The BFF's settings: bound from <c>Identity:Client</c>, code over the section, checked at boot, handed to the handlers.</summary>
public sealed class BffOptionsTests
{
    private static readonly Dictionary<string, string?> Section = new()
    {
        ["Identity:Client:Authority"] = "https://id.example.test/",
        ["Identity:Client:ClientId"] = "desk",
        ["Identity:Client:ClientSecret"] = "test-only-secret",
        ["Identity:Client:Scopes:0"] = "openid",
        ["Identity:Client:Scopes:1"] = "offline_access",
        ["Identity:Client:RoutePrefix"] = "/session-routes",
    };

    [Fact]
    public void Settings_ShouldBindTheSection_ThenLetCodeWin()
    {
        using var provider = Provider(Section, options => options.ClientId = "desk-from-code");

        var options = provider.GetRequiredService<IOptions<IdentityBffOptions>>().Value;

        options.Authority.Should().Be("https://id.example.test/");
        options.ClientId.Should().Be("desk-from-code", "code configuration runs after the section");
        options.EffectiveScopes.Should().Equal("openid", "offline_access");
        options.EffectiveApiPathPrefixes.Should().Equal("/api");
        options.SessionLifetime.Should().Be(TimeSpan.FromHours(12));
    }

    [Fact]
    public void Defaults_ShouldAskForProfileEmailPhoneAndRefresh()
    {
        using var provider = Provider(new() { ["Identity:Client:Authority"] = "https://id.example.test", ["Identity:Client:ClientId"] = "desk" });

        var options = provider.GetRequiredService<IOptions<IdentityBffOptions>>().Value;

        options.EffectiveScopes.Should().Equal("openid", "profile", "email", "phone", "offline_access");
        options.RoutePrefix.Should().Be("/api/identity");
        options.PostSignOutRedirectPath.Should().Be("/");
    }

    [Theory]
    [InlineData("Authority", "", "Authority")]
    [InlineData("Authority", "http://id.example.test", "HTTPS")]
    [InlineData("ClientId", "", "ClientId")]
    [InlineData("RoutePrefix", "auth", "RoutePrefix")]
    [InlineData("RoutePrefix", "/api/identity/", "RoutePrefix")]
    [InlineData("PostSignOutRedirectPath", "https://elsewhere.example.test/", "PostSignOutRedirectPath")]
    [InlineData("Scopes:0", "profile", "openid")]
    public void Validation_ShouldRefuseTheBoot(string key, string value, string expected)
    {
        var settings = new Dictionary<string, string?>(Section) { ["Identity:Client:" + key] = value };
        if (key == "Scopes:0")
            settings.Remove("Identity:Client:Scopes:1");
        using var provider = Provider(settings);

        var read = () => provider.GetRequiredService<IOptions<IdentityBffOptions>>().Value;

        read.Should().Throw<OptionsValidationException>().Which.Message.Should().Contain(expected);
    }

    [Fact]
    public void Handlers_ShouldTakeTheSettings_CodeFlowWithPkceUnderThePrefix()
    {
        using var provider = Provider(Section);

        var handler = provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(BffAuthenticationSchemeConstants.OpenIdConnect);
        var cookie = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(BffAuthenticationSchemeConstants.Cookie);

        handler.Authority.Should().Be("https://id.example.test/");
        handler.ClientId.Should().Be("desk");
        handler.ResponseType.Should().Be("code");
        handler.ResponseMode.Should().Be("query");
        handler.UsePkce.Should().BeTrue();
        handler.MapInboundClaims.Should().BeFalse();
        handler.CallbackPath.Value.Should().Be("/session-routes/callback");
        handler.Scope.Should().Equal("openid", "offline_access");
        cookie.LoginPath.Value.Should().Be("/session-routes/sign-in");
        cookie.SlidingExpiration.Should().BeFalse("the session's end is absolute");
        cookie.Cookie.HttpOnly.Should().BeTrue();
        cookie.Cookie.Name.Should().Be(".app.session");
    }

    private static ServiceProvider Provider(Dictionary<string, string?> settings, Action<IdentityBffOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddLogging();
        services.AddIdentityBff(configure);
        return services.BuildServiceProvider();
    }
}
