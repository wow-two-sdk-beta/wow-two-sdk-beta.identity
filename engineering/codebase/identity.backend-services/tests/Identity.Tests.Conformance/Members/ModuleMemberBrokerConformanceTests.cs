using Identity.Tests.Conformance.Notices;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Members;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace Identity.Tests.Conformance.Members;

/// <summary>
/// The member cases against Form 1: the Module wired as a product wires it (<c>AddUserAccounts().AddOrganizations()</c> with the
/// bot's links) over PostgreSQL in Testcontainers, the in-process broker reading the memberships and the bot's links.
/// </summary>
public sealed class ModuleMemberBrokerConformanceTests : MemberBrokerConformance, IAsyncLifetime
{
    private const string BotUserName = "ocharo_conformance_bot";

    private string _connectionString = string.Empty;
    private ServiceProvider _provider = null!;
    private AsyncServiceScope _scope;
    private long _nextPhone = 7100;

    /// <inheritdoc />
    protected override IMemberBroker Broker => _scope.ServiceProvider.GetRequiredService<IMemberBroker>();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _connectionString = await PostgresTestServer.CreateDatabaseAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDatabaseSettings(_connectionString);
        services.AddSingleton<ITelegramBotClient>(new ConformanceTelegramBot());
        services.AddUserAccounts()
            .AddOrganizations()
            .AddTelegramLinks(bot =>
            {
                bot.IsEnabled = true;
                bot.BotToken = "123456:conformance-token";
                bot.BotUserName = BotUserName;
                bot.WebhookSecret = "conformance-secret";
            });
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
    protected override async Task<(Guid OrganizationId, string OwnerSub)> CreateOrganizationAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        var owner = await CreatePersonAsync(scope.ServiceProvider, isLinked: true);
        var organization = new IdentityOrganization { Name = "Conformance", Slug = "conformance-" + Guid.NewGuid().ToString("N")[..12] };
        (await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateAsync(organization, owner)).ThrowIfFailed();
        return (organization.Id, owner.Id.ToString());
    }

    /// <inheritdoc />
    protected override async Task<string> AddMemberAsync(Guid organizationId, string role, bool isLinked)
    {
        await using var scope = _provider.CreateAsyncScope();
        var organizations = scope.ServiceProvider.GetRequiredService<OrganizationService>();
        var person = await CreatePersonAsync(scope.ServiceProvider, isLinked);
        var organization = await organizations.FindByIdAsync(organizationId) ?? throw new InvalidOperationException("No such organization.");
        (await organizations.AddMemberAsync(organization, person, role)).ThrowIfFailed();
        return person.Id.ToString();
    }

    private async Task<IdentityUser> CreatePersonAsync(IServiceProvider services, bool isLinked)
    {
        var number = ++_nextPhone;
        var phone = "+99891" + number.ToString("D7", System.Globalization.CultureInfo.InvariantCulture);
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
