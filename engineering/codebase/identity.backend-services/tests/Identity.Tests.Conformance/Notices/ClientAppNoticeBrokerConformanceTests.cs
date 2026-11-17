using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Telegram.Bot;
using WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2;
using WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2ClientCredentials;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Notices;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;
using WoW.Two.Sdk.Beta.Be.Testing;

namespace Identity.Tests.Conformance.Notices;

/// <summary>
/// The app notice cases against Forms 2 and 3: the Client's broker, holding a client-credentials token from the be sdk's
/// handler, calls the real service in process over PostgreSQL, its bot client keeping its messages.
/// </summary>
public sealed class ClientAppNoticeBrokerConformanceTests : AppNoticeBrokerConformance, IAsyncLifetime
{
    private const string Instance = "https://localhost/";
    private const string Audience = "https://localhost/api/identity/";
    private const string App = "conformance-events";
    private const string Secret = "conformance-test-only-secret";
    private const string BotUserName = "ocharo_conformance_bot";

    private readonly ConformanceTelegramBot _bot = new();
    private readonly Dictionary<string, long> _chats = [];
    private string _connectionString = string.Empty;
    private WebApiTestHost<Program> _host = null!;
    private ServiceProvider _caller = null!;
    private long _nextChat = 8000;

    /// <inheritdoc />
    protected override IAppNoticeBroker Broker => _caller.GetRequiredService<IAppNoticeBroker>();

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
                ["Identity:Notices:Audience"] = Audience,
                ["Identity:Server:Resources:0:Name"] = "identity",
                ["Identity:Server:Resources:0:Uri"] = Audience,
                ["Identity:Server:Resources:0:Scopes:0"] = AppNoticeConstants.Scope,
                ["Identity:Server:Clients:0:ClientId"] = App,
                ["Identity:Server:Clients:0:ClientSecret"] = Secret,
                ["Identity:Server:Clients:0:Grants:0"] = "client_credentials",
                ["Identity:Server:Clients:0:Resources:0"] = "identity",
                ["Identity:Server:Clients:0:Scopes:0"] = AppNoticeConstants.Scope,
                ["Identity:Telegram:IsEnabled"] = "true",
                ["Identity:Telegram:BotToken"] = "123456:conformance-token",
                ["Identity:Telegram:BotUserName"] = BotUserName,
                ["Identity:Telegram:WebhookSecret"] = "conformance-secret",
                ["Identity:Otp:HashKey"] = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=",
            }),
            ConfigureServicesHook = services =>
            {
                services.RemoveAll<ITelegramBotClient>();
                services.AddSingleton<ITelegramBotClient>(_bot);
            },
        };
        _host.Clock.SetUtcNow(DateTimeOffset.UtcNow);

        // Building a client starts the host: migrations and the client seeding.
        using (_host.CreateClient())
        {
        }

        var services = new ServiceCollection();
        services.AddHttpClient(OAuth2HttpClientNames.TokenEndpoint).ConfigurePrimaryHttpMessageHandler(() => _host.Server.CreateHandler());
        services.AddIdentityAppNotices(new Uri(Instance))
            .ConfigurePrimaryHttpMessageHandler(() => _host.Server.CreateHandler())
            .AddOAuth2ClientCredentials(options =>
            {
                options.TokenEndpoint = new Uri(Instance + "connect/token");
                options.ClientId = App;
                options.ClientSecret = Secret;
                options.Resource = Audience;
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
    protected override Task<string> CreateLinkedPersonAsync() => CreatePersonAsync(isLinked: true);

    /// <inheritdoc />
    protected override Task<string> CreateUnlinkedPersonAsync() => CreatePersonAsync(isLinked: false);

    /// <inheritdoc />
    protected override async Task MuteAsync(string sub)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IAppNoticeRepository>().MuteAsync(Guid.Parse(sub), App, DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    protected override Task<int> CountDeliveredAsync(string sub) => Task.FromResult(_chats.TryGetValue(sub, out var chat) ? _bot.CountTo(chat) : 0);

    private async Task<string> CreatePersonAsync(bool isLinked)
    {
        var chat = ++_nextChat;
        var phone = "+99890" + chat.ToString("D7", System.Globalization.CultureInfo.InvariantCulture);
        await using var scope = _host.Services.CreateAsyncScope();
        var person = new IdentityUser { UserName = phone, DisplayName = phone, PhoneNumber = phone, IsPhoneNumberConfirmed = true };
        (await scope.ServiceProvider.GetRequiredService<UserAccountService>().CreateAsync(person)).ThrowIfFailed();
        if (isLinked)
        {
            await scope.ServiceProvider.GetRequiredService<ITelegramLinkRepository>().SaveLinkAsync(new IdentityTelegramLink
            {
                Id = Guid.CreateVersion7(),
                BotUserName = BotUserName,
                PhoneNumber = phone,
                ChatId = chat,
                TelegramUserId = chat,
                LinkedAt = DateTimeOffset.UtcNow,
            });
        }

        var sub = person.Id.ToString();
        _chats[sub] = chat;
        return sub;
    }
}
