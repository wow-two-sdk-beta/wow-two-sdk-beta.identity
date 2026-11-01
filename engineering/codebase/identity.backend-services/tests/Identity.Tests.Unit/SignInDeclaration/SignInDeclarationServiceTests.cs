using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignInDeclaration;
using WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.SignInDeclaration;

/// <summary>
/// The declaration read from the live settings: the look from <c>Identity:Pages</c> under a registered client's own, the
/// password method only with its slices, sign-up and verifications from their sections, and a refused start for a look
/// invalid on its own, the instance's or a client's.
/// </summary>
public sealed class SignInDeclarationServiceTests
{
    private static readonly OidcClientOptions Desk = new()
    {
        ClientId = "desk",
        ClientSecret = "test-only-secret",
        RedirectUris = ["https://desk.example.test/signin-oidc"],
        Theme = "ocharo",
        BackgroundUrl = "https://cdn.example.test/desk.jpg",
    };

    [Fact]
    public void Build_ShouldAnswerTheInstanceDefaults_WhenNoSettingsExist()
    {
        using var provider = Build([]);

        var declaration = Declare(provider, clientId: null);

        declaration.BrandName.Should().Be("Identity");
        declaration.Theme.Should().Be("platform");
        declaration.BackgroundUrl.Should().BeNull();
        declaration.IsSignUpEnabled.Should().BeFalse();
        declaration.Methods.Select(method => method.Id).Should().Equal("password");
        declaration.Requirements.Should().Be(new SignInRequirementsDto());
    }

    [Fact]
    public void Build_ShouldReadTheSettingsLiveFromTheirSections()
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Identity:Pages:BrandName"] = "Ocharo",
            ["Identity:Pages:Theme"] = "ocharo",
            ["Identity:Pages:BackgroundUrl"] = "/backgrounds/ocharo.jpg",
            ["Identity:SignUp:IsEnabled"] = "true",
            ["Identity:SignIn:IsConfirmedEmailRequired"] = "true",
            ["Identity:Password:MinLength"] = "20",
        });

        var declaration = Declare(provider, clientId: null, locale: "ru-RU");

        declaration.BrandName.Should().Be("Ocharo");
        declaration.Theme.Should().Be("ocharo");
        declaration.BackgroundUrl.Should().Be("/backgrounds/ocharo.jpg");
        declaration.IsSignUpEnabled.Should().BeTrue();
        declaration.Requirements.IsConfirmedEmailRequired.Should().BeTrue();
        declaration.Methods.Single().PasswordRules![0].Params!["min"].Should().Be(20);
        declaration.Locale.Should().Be("ru");
    }

    [Fact]
    public void Build_ShouldTakeARegisteredClientsLook_AndTheInstancesForAnyOther()
    {
        using var provider = Build(new Dictionary<string, string?> { ["Identity:Pages:BackgroundUrl"] = "/backgrounds/instance.jpg" }, withClients: true);

        var desk = Declare(provider, "desk");
        var unknown = Declare(provider, "nobody");
        var differentCase = Declare(provider, "DESK");
        var none = Declare(provider, clientId: null);

        desk.Theme.Should().Be("ocharo");
        desk.BackgroundUrl.Should().Be("https://cdn.example.test/desk.jpg");
        unknown.Should().BeEquivalentTo(none, "an unknown client must not tell which clients exist");
        differentCase.Should().BeEquivalentTo(none, "client ids compare exactly");
        none.Theme.Should().Be("platform");
        none.BackgroundUrl.Should().Be("/backgrounds/instance.jpg");
    }

    [Fact]
    public void Build_ShouldKeepTheInstancesBackground_WhenAClientNamesOnlyATheme()
    {
        using var provider = Build(new Dictionary<string, string?> { ["Identity:Pages:BackgroundUrl"] = "/backgrounds/instance.jpg" }, withClients: true, desk: Desk with { BackgroundUrl = null });

        var desk = Declare(provider, "desk");

        desk.Theme.Should().Be("ocharo");
        desk.BackgroundUrl.Should().Be("/backgrounds/instance.jpg");
    }

    [Fact]
    public void Build_ShouldLeaveThePasswordMethodOut_WithoutThePasswordSlice()
    {
        using var provider = Build([], withPasswords: false);

        Declare(provider, clientId: null).Methods.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Identity:Pages:Theme", "Ocharo", "Identity:Pages:Theme")]
    [InlineData("Identity:Pages:Theme", "", "Identity:Pages:Theme")]
    [InlineData("Identity:Pages:BrandName", " ", "Identity:Pages:BrandName")]
    [InlineData("Identity:Pages:BackgroundUrl", "http://cdn.example.test/bg.jpg", "Identity:Pages:BackgroundUrl")]
    [InlineData("Identity:Pages:BackgroundUrl", "//cdn.example.test/bg.jpg", "Identity:Pages:BackgroundUrl")]
    public void Options_ShouldRefuseTheStart_WhenTheLookIsInvalidOnItsOwn(string key, string value, string expected)
    {
        using var provider = Build(new Dictionary<string, string?> { [key] = value });

        var act = () => provider.GetRequiredService<IOptions<HostedPageOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage($"*{expected}*");
    }

    [Fact]
    public void ServerOptions_ShouldRefuseAClientLook_ThatIsNoThemeIdOrSafeBackground()
    {
        var options = new OidcServerOptions { Clients = [Desk with { Theme = "Ocharo Dark", BackgroundUrl = "javascript:alert(1)" }] };

        var result = new OidcServerOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(failure => failure.Contains("Client 'desk' has a Theme", StringComparison.Ordinal));
        result.Failures.Should().Contain(failure => failure.Contains("Client 'desk' has a BackgroundUrl", StringComparison.Ordinal));
        new OidcServerOptionsValidator().Validate(null, new OidcServerOptions { Clients = [Desk] }).Should().Be(ValidateOptionsResult.Success);
    }

    private static SignInOptionsDto Declare(ServiceProvider provider, string? clientId, string? locale = null)
    {
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<SignInDeclarationService>().Build(clientId, locale);
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings, bool withPasswords = true, bool withClients = false, OidcClientOptions? desk = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        var identity = services.AddUserAccounts();
        if (withPasswords)
            identity.AddArgon2Passwords().AddSignIn();
        identity.AddAccountEndpoints().AddSignInDeclaration();
        if (withClients)
        {
            services.AddOptions<OidcServerOptions>().Configure(options => options.Clients = [desk ?? Desk]);
            identity.AddOidcClientPageLooks();
        }

        var provider = services.BuildServiceProvider();
        provider.GetService<IServiceProviderIsService>()!.IsService(typeof(SignInService)).Should().Be(withPasswords);
        provider.GetService<IServiceProviderIsService>()!.IsService(typeof(UserPasswordService)).Should().Be(withPasswords);
        return provider;
    }
}
