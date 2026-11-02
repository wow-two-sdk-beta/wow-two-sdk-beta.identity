using System.Net;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Identity.Core;

namespace Identity.Tests.E2E.Oidc;

/// <summary>
/// An app's refresh chain ends with the sign-in session behind it: a fixed session's end, the extended one when the
/// person asked to stay signed in, and a sliding session's end, which each refresh moves; each token response names it.
/// </summary>
public sealed class OidcSessionLifetimeTests
{
    private const string Password = "e2e-correct-horse-battery";

    [Fact]
    public async Task FixedSession_ShouldEndTheChain_AtTheDefaultOrTheExtendedLifetime()
    {
        await using var server = new OidcHost();
        using var brief = server.CreateClient();
        await brief.SignUpAndSignInAsync($"brief-{Guid.NewGuid():N}@example.test", Password);
        var briefTokens = await brief.SignInToAppAsync();
        briefTokens.SessionExpiresIn!.Value.Should().BeCloseTo((long)TimeSpan.FromHours(12).TotalSeconds, 5, "a fixed session lasts Lifetime");

        server.Host.Clock.Advance(TimeSpan.FromHours(1));
        using var refreshed = await brief.RefreshAsync(briefTokens.RefreshToken);
        (await TokenSet.ReadAsync(refreshed)).SessionExpiresIn!.Value.Should().BeCloseTo((long)TimeSpan.FromHours(11).TotalSeconds, 5, "a refresh never extends a fixed session");

        using var kept = server.CreateClient();
        var email = $"kept-{Guid.NewGuid():N}@example.test";
        await kept.SignUpAndSignInAsync(email, Password);
        await kept.SignInAsync(email, Password, isPersistent: true);
        (await kept.SignInToAppAsync()).SessionExpiresIn!.Value.Should().BeCloseTo((long)TimeSpan.FromDays(30).TotalSeconds, 5, "staying signed in takes ExtendedLifetime");
    }

    [Fact]
    public async Task SlidingSession_ShouldMoveTheChainsEnd_WithEachRefresh_AndEndOnceIdle()
    {
        await using var server = new OidcHost(new Dictionary<string, string?>
        {
            ["Identity:Sessions:Expiration"] = "Sliding",
            ["Identity:Sessions:SlidingLifetime"] = "5.00:00:00",
        });
        using var client = server.CreateClient();
        var userId = await client.SignUpAndSignInAsync($"slide-{Guid.NewGuid():N}@example.test", Password);
        var tokens = await client.SignInToAppAsync();
        var fiveDays = (long)TimeSpan.FromDays(5).TotalSeconds;
        tokens.SessionExpiresIn!.Value.Should().BeCloseTo(fiveDays, 5);

        server.Host.Clock.Advance(TimeSpan.FromDays(3));
        using var refreshed = await client.RefreshAsync(tokens.RefreshToken);
        var renewed = await TokenSet.ReadAsync(refreshed);
        renewed.SessionExpiresIn!.Value.Should().BeCloseTo(fiveDays, 5, "the refresh used the session, which moved its end");
        (await SessionEndAsync(server, userId)).Should().BeCloseTo(server.Host.Clock.GetUtcNow().AddDays(5), TimeSpan.FromSeconds(5));

        server.Host.Clock.Advance(TimeSpan.FromDays(6));
        using var idle = await client.RefreshAsync(renewed.RefreshToken);
        idle.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a session left unused past SlidingLifetime has ended");
    }

    [Fact]
    public async Task Declaration_ShouldOfferToStaySignedIn_OnlyWhileSessionsAreFixed()
    {
        await using var fixedServer = new OidcHost();
        using var fixedClient = fixedServer.CreateClient();
        (await IsPersistenceOfferedAsync(fixedClient)).Should().BeTrue();

        await using var slidingServer = new OidcHost(new Dictionary<string, string?> { ["Identity:Sessions:Expiration"] = "Sliding" });
        using var slidingClient = slidingServer.CreateClient();
        (await IsPersistenceOfferedAsync(slidingClient)).Should().BeFalse();
    }

    private static async Task<bool> IsPersistenceOfferedAsync(OidcTestClient client)
    {
        using var response = await client.Http.GetAsync("/api/account/sign-in-options");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement.TryGetProperty("data", out var data) ? data : document.RootElement;
        return root.GetProperty("isPersistenceOffered").GetBoolean();
    }

    private static async Task<DateTimeOffset> SessionEndAsync(OidcHost server, Guid userId)
    {
        await using var scope = server.Host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Sessions
            .Where(session => session.UserId == userId)
            .Select(session => session.ExpiresAt)
            .SingleAsync();
    }
}
