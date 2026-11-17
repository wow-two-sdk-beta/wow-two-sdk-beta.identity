using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Notices;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace Identity.Tests.Conformance.Notices;

/// <summary>
/// The app notice cases against Form 1: the Module wired as a product wires it (<c>AddUserAccounts().AddAppNotices()</c>) over
/// PostgreSQL in Testcontainers, the in-process broker sending through a bot client that keeps its messages.
/// </summary>
public sealed class ModuleAppNoticeBrokerConformanceTests : AppNoticeBrokerConformance, IAsyncLifetime
{
    private const string App = "conformance-app";
    private const string BotUserName = "ocharo_conformance_bot";

    private readonly ConformanceTelegramBot _bot = new();
    private readonly Dictionary<string, long> _chats = [];
    private string _connectionString = string.Empty;
    private ServiceProvider _provider = null!;
    private AsyncServiceScope _scope;
    private long _nextChat = 7000;

    /// <inheritdoc />
    protected override IAppNoticeBroker Broker => _scope.ServiceProvider.GetRequiredService<IAppNoticeBroker>();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _connectionString = await PostgresTestServer.CreateDatabaseAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDatabaseSettings(_connectionString);
        services.AddSingleton<ITelegramBotClient>(_bot);
        services.AddUserAccounts()
            .AddTelegramLinks(bot =>
            {
                bot.IsEnabled = true;
                bot.BotToken = "123456:conformance-token";
                bot.BotUserName = BotUserName;
                bot.WebhookSecret = "conformance-secret";
            })
            .AddAppNotices(notices => notices.LocalClientId = App);
        _provider = services.BuildServiceProvider();
        (await _provider.ApplyMigrationSetsAsync("test")).ValueOrThrow();
        _scope = _provider.CreateAsyncScope();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
        await PostgresTestServer.DropDatabaseAsync(_connectionString);
    }

    /// <inheritdoc />
    protected override Task<string> CreateLinkedPersonAsync() => CreatePersonAsync(isLinked: true);

    /// <inheritdoc />
    protected override Task<string> CreateUnlinkedPersonAsync() => CreatePersonAsync(isLinked: false);

    /// <inheritdoc />
    protected override async Task MuteAsync(string sub)
    {
        await using var scope = _provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IAppNoticeRepository>().MuteAsync(Guid.Parse(sub), App, DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    protected override Task<int> CountDeliveredAsync(string sub) => Task.FromResult(_chats.TryGetValue(sub, out var chat) ? _bot.CountTo(chat) : 0);

    private async Task<string> CreatePersonAsync(bool isLinked)
    {
        var chat = ++_nextChat;
        var phone = "+99890" + chat.ToString("D7", System.Globalization.CultureInfo.InvariantCulture);
        await using var scope = _provider.CreateAsyncScope();
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

/// <summary>Stops a test on a refused account creation.</summary>
internal static class IdentityResultAssertions
{
    public static void ThrowIfFailed(this IdentityResult result)
    {
        if (!result.IsSuccess)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Description)));
    }
}
