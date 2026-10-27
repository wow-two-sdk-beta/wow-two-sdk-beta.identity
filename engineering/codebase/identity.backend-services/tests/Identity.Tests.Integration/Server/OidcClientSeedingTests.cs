using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Server;

/// <summary>Seeding <c>Identity:Server:Clients</c> through OpenIddict's EF Core stores on the Module's schema: idempotent, hashed, owned.</summary>
public sealed class OidcClientSeedingTests : IAsyncLifetime
{
    private const string Secret = "test-only-secret-1";
    private readonly IdentityTestDatabase _database = new();
    private ServiceProvider _provider = null!;

    private OidcServerOptions Options => _provider.GetRequiredService<OidcServerOptions>();

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        _database.Register(services);
        services.AddUserAccounts()
            .AddOidcServer(options =>
            {
                // No host environment here reads as a deployed boot, which needs a pinned issuer.
                options.Issuer = new Uri("https://id.example.test/");
                options.Clients.Add(Client(Secret, "https://app.example.test/signin-oidc"));
            });
        _provider = services.BuildServiceProvider();
        await IdentityTestDatabase.MigrateAsync(_provider);
    }

    [Fact]
    public async Task Seed_ShouldStoreTheClientWithAHashedSecret_AndLeaveItAloneWhenUnchanged()
    {
        await SeedAsync();
        var first = await RowAsync();

        await SeedAsync();
        var second = await RowAsync();

        first.ClientSecret.Should().NotBeNullOrEmpty().And.NotContain(Secret);
        (await ValidatesAsync(Secret)).Should().BeTrue();
        second.ConcurrencyToken.Should().Be(first.ConcurrencyToken, "an unchanged client is not rewritten");
        second.ClientSecret.Should().Be(first.ClientSecret);
    }

    [Fact]
    public async Task Seed_ShouldUpdateAChangedClient_KeepingAMatchingSecretsHash()
    {
        await SeedAsync();
        var before = await RowAsync();

        Options.Clients[0] = Client(Secret, "https://app.example.test/callback");
        await SeedAsync();
        var moved = await RowAsync();

        moved.RedirectUris.Should().Contain("https://app.example.test/callback").And.NotContain("signin-oidc");
        moved.ClientSecret.Should().Be(before.ClientSecret);

        Options.Clients[0] = Client("test-only-secret-2", "https://app.example.test/callback");
        await SeedAsync();

        (await ValidatesAsync("test-only-secret-2")).Should().BeTrue();
        (await ValidatesAsync(Secret)).Should().BeFalse();
    }

    [Fact]
    public async Task Seed_ShouldDeleteItsOwnClientsThatLeftConfiguration_AndNoOthers()
    {
        await SeedAsync();
        await using (var scope = _provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>().CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = "registered-elsewhere",
                ClientSecret = "test-only-secret-3",
                ClientType = OpenIddictConstants.ClientTypes.Confidential,
            });
        }

        Options.Clients.Clear();
        await SeedAsync();

        await using var check = _provider.CreateAsyncScope();
        var clients = await check.ServiceProvider.GetRequiredService<IdentityDbContext>().OidcApplications.Select(a => a.ClientId).ToListAsync();
        clients.Should().Equal("registered-elsewhere");
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _database.DisposeAsync();
    }

    private static OidcClientOptions Client(string secret, string redirect) => new()
    {
        ClientId = "app",
        ClientSecret = secret,
        RedirectUris = [redirect],
        PostLogoutRedirectUris = ["https://app.example.test/signed-out"],
    };

    private async Task SeedAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OidcClientSeeder>().SeedAsync(CancellationToken.None);
    }

    private async Task<IdentityOidcApplication> RowAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().OidcApplications.AsNoTracking().SingleAsync(a => a.ClientId == "app");
    }

    private async Task<bool> ValidatesAsync(string secret)
    {
        await using var scope = _provider.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        return await applications.ValidateClientSecretAsync((await applications.FindByClientIdAsync("app"))!, secret);
    }
}
