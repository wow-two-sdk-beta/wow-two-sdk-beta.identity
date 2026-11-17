using Identity.Tests.Conformance.Notices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Telegram.Bot;
using WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2;
using WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2ClientCredentials;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Members;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;
using WoW.Two.Sdk.Beta.Be.Testing;

namespace Identity.Tests.Conformance.Members;

/// <summary>
/// The member cases against Forms 2 and 3: the Client's broker, holding a client-credentials token from the be sdk's
/// handler, calls the real service in process over PostgreSQL.
/// </summary>
public sealed class ClientMemberBrokerConformanceTests : MemberBrokerConformance, IAsyncLifetime
{
    private const string Instance = "https://localhost/";
    private const string Audience = "https://localhost/api/identity/";
    private const string App = "conformance-events";
    private const string Secret = "conformance-test-only-secret";
    private const string BotUserName = "ocharo_conformance_bot";

    private string _connectionString = string.Empty;
    private WebApiTestHost<Program> _host = null!;
    private ServiceProvider _caller = null!;
    private long _nextPhone = 8100;

    /// <inheritdoc />
    protected override IMemberBroker Broker => _caller.GetRequiredService<IMemberBroker>();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _connectionString = await PostgresTestServer.CreateDatabaseAsync();
        _host = new WebApiTestHost<Program>
        {
            ConfigureConfigurationHook = configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseSettings:ConnectionString"] = _connectionString,
                ["AllowedHosts"] = "localhost",
                ["Logging:LogLevel:Default"] = "Warning",
                ["Identity:Server:Issuer"] = Instance,
                ["Identity:Members:Audience"] = Audience,
                ["Identity:Server:Resources:0:Name"] = "identity",
                ["Identity:Server:Resources:0:Uri"] = Audience,
                ["Identity:Server:Resources:0:Scopes:0"] = MemberConstants.Scope,
                ["Identity:Server:Clients:0:ClientId"] = App,
                ["Identity:Server:Clients:0:ClientSecret"] = Secret,
                ["Identity:Server:Clients:0:Grants:0"] = "client_credentials",
                ["Identity:Server:Clients:0:Resources:0"] = "identity",
                ["Identity:Server:Clients:0:Scopes:0"] = MemberConstants.Scope,
                ["Identity:Telegram:IsEnabled"] = "true",
                ["Identity:Telegram:BotToken"] = "123456:conformance-token",
                ["Identity:Telegram:BotUserName"] = BotUserName,
                ["Identity:Telegram:WebhookSecret"] = "conformance-secret",
                ["Identity:Otp:HashKey"] = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=",
            }),
            ConfigureServicesHook = services =>
            {
                services.RemoveAll<ITelegramBotClient>();
                services.AddSingleton<ITelegramBotClient>(new ConformanceTelegramBot());
            },
        };
        _host.Clock.SetUtcNow(DateTimeOffset.UtcNow);

        // Building a client starts the host: migrations and the client seeding.
        using (_host.CreateClient())
        {
        }

        var services = new ServiceCollection();
        services.AddHttpClient(OAuth2HttpClientNames.TokenEndpoint).ConfigurePrimaryHttpMessageHandler(() => _host.Server.CreateHandler());
        services.AddIdentityMembers(new Uri(Instance))
            .ConfigurePrimaryHttpMessageHandler(() => _host.Server.CreateHandler())
            .AddOAuth2ClientCredentials(options =>
            {
                options.TokenEndpoint = new Uri(Instance + "connect/token");
                options.ClientId = App;
                options.ClientSecret = Secret;
                options.Resource = Audience;
                options.Scope = MemberConstants.Scope;
            });
        _caller = services.BuildServiceProvider();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await _caller.DisposeAsync();
        await _host.DisposeAsync();
        await PostgresTestServer.DropDatabaseAsync(_connectionString);
    }

    /// <inheritdoc />
    protected override async Task<(Guid OrganizationId, string OwnerSub)> CreateOrganizationAsync()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var owner = await CreatePersonAsync(scope.ServiceProvider, isLinked: true);
        var organization = new IdentityOrganization { Name = "Conformance", Slug = "conformance-" + Guid.NewGuid().ToString("N")[..12] };
        (await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateAsync(organization, owner)).ThrowIfFailed();
        return (organization.Id, owner.Id.ToString());
    }

    /// <inheritdoc />
    protected override async Task<string> AddMemberAsync(Guid organizationId, string role, bool isLinked)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var organizations = scope.ServiceProvider.GetRequiredService<OrganizationService>();
        var person = await CreatePersonAsync(scope.ServiceProvider, isLinked);
        var organization = await organizations.FindByIdAsync(organizationId) ?? throw new InvalidOperationException("No such organization.");
        (await organizations.AddMemberAsync(organization, person, role)).ThrowIfFailed();
        return person.Id.ToString();
    }

    private async Task<IdentityUser> CreatePersonAsync(IServiceProvider services, bool isLinked)
    {
        // The host's clock stands still; each person joins a second after the last, so "earliest first" has one answer.
        _host.Clock.Advance(TimeSpan.FromSeconds(1));
        var number = ++_nextPhone;
        var phone = "+99892" + number.ToString("D7", System.Globalization.CultureInfo.InvariantCulture);
        var person = new IdentityUser { UserName = phone, DisplayName = phone, PhoneNumber = phone, IsPhoneNumberConfirmed = true };
        (await services.GetRequiredService<UserAccountService>().CreateAsync(person)).ThrowIfFailed();
        if (isLinked)
        {
            await services.GetRequiredService<ITelegramLinkRepository>().SaveLinkAsync(new IdentityTelegramLink
            {
                Id = Guid.CreateVersion7(),
                BotUserName = BotUserName,
                PhoneNumber = phone,
                ChatId = number,
                TelegramUserId = number,
                LinkedAt = DateTimeOffset.UtcNow,
            });
        }

        return person;
    }
}
