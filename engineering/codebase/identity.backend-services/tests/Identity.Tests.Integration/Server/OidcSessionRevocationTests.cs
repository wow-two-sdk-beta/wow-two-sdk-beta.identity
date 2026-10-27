using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;
using WoW.Two.Sdk.Beta.Be.Identity.OidcServer;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Server;

/// <summary>An OIDC grant hangs off the sign-in session that made it: revoking the session revokes its authorizations and their tokens, and nothing else.</summary>
public sealed class OidcSessionRevocationTests : IAsyncLifetime
{
    private readonly IdentityTestDatabase _database = new();
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        _database.Register(services);
        services.AddUserAccounts()
            .AddSessions()
            .AddOidcServer(options =>
            {
                options.Issuer = new Uri("https://id.example.test/");
                options.Clients.Add(new OidcClientOptions { ClientId = "app", ClientSecret = "test-only-secret", RedirectUris = ["https://app.example.test/cb"] });
            });
        _provider = services.BuildServiceProvider();
        await IdentityTestDatabase.MigrateAsync(_provider);
        await using var scope = _provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OidcClientSeeder>().SeedAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RevokingASession_ShouldRevokeItsAuthorizationsAndTokens_Only()
    {
        var user = new IdentityUser { UserName = "faye", Email = "faye@example.test" };
        await using (var scope = _provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<UserAccountService>().CreateAsync(user);
        var ended = await StartAsync(user);
        var kept = await StartAsync(user);
        var (endedAuthorization, endedToken) = await GrantAsync(user, ended.Id);
        var (keptAuthorization, keptToken) = await GrantAsync(user, kept.Id);

        await using (var scope = _provider.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<UserSessionService>().RevokeAsync(user.Id, ended.Id)).Should().BeTrue();

        await using var read = _provider.CreateAsyncScope();
        var authorizations = read.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var tokens = read.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        (await authorizations.GetStatusAsync((await authorizations.FindByIdAsync(endedAuthorization))!)).Should().Be(Statuses.Revoked);
        (await tokens.GetStatusAsync((await tokens.FindByIdAsync(endedToken))!)).Should().Be(Statuses.Revoked, "the refresh chain dies with the session");
        (await authorizations.GetStatusAsync((await authorizations.FindByIdAsync(keptAuthorization))!)).Should().Be(Statuses.Valid);
        (await tokens.GetStatusAsync((await tokens.FindByIdAsync(keptToken))!)).Should().Be(Statuses.Valid);
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _database.DisposeAsync();
    }

    private async Task<IdentitySession> StartAsync(IdentityUser user)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<UserSessionService>().StartAsync(user, ["pwd"]);
    }

    /// <summary>An authorization made as the authorize endpoint makes it, and a refresh token under it.</summary>
    private async Task<(string Authorization, string Token)> GrantAsync(IdentityUser user, Guid sessionId)
    {
        await using var scope = _provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var applications = services.GetRequiredService<IOpenIddictApplicationManager>();
        var applicationId = (await applications.GetIdAsync((await applications.FindByClientIdAsync("app"))!))!;
        var authorizations = services.GetRequiredService<IOpenIddictAuthorizationManager>();
        var identity = new ClaimsIdentity("Tests");
        identity.SetScopes(Scopes.OpenId, Scopes.OfflineAccess);
        var principal = new ClaimsPrincipal(identity);

        await OidcSessionRevocationHandler.AttachAsync(authorizations, applicationId, user.Id.ToString("D"), sessionId, principal, DateTimeOffset.UtcNow, CancellationToken.None);

        var authorizationId = principal.GetAuthorizationId()!;
        var tokens = services.GetRequiredService<IOpenIddictTokenManager>();
        var token = await tokens.CreateAsync(new OpenIddictTokenDescriptor
        {
            ApplicationId = applicationId,
            AuthorizationId = authorizationId,
            Subject = user.Id.ToString("D"),
            Status = Statuses.Valid,
            Type = TokenTypeIdentifiers.RefreshToken,
            CreationDate = DateTimeOffset.UtcNow,
            ExpirationDate = DateTimeOffset.UtcNow.AddHours(12),
        });
        return (authorizationId, (await tokens.GetIdAsync(token))!);
    }
}
