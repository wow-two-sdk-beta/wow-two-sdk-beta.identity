using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.RefreshTokens;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Accounts;

/// <summary>Refresh tokens: rotation, reuse detection, expiry, stamp rotation and revocation.</summary>
public sealed class RefreshTokenTests
{
    private static AccountsHost CreateHost() => AccountsHost.Create(identity => identity.AddRefreshTokens(o => o.Lifetime = TimeSpan.FromDays(7)));

    [Fact]
    public async Task Redeem_ShouldRotateAndRevokeTheFamilyWhenASpentTokenReturns()
    {
        await using var host = CreateHost();
        var user = await host.CreateUserAsync("walt");
        await using var scope = host.Scope();
        var refresh = scope.ServiceProvider.GetRequiredService<RefreshTokenService>();

        var first = await refresh.IssueAsync(user);
        first.ExpiresAt.Should().Be(host.Time.GetUtcNow().AddDays(7));

        var rotated = await refresh.RedeemAsync(first.Token);
        rotated.IsSuccess.Should().BeTrue();
        rotated.User!.Id.Should().Be(user.Id);
        rotated.Token!.Token.Should().NotBe(first.Token);

        (await refresh.RedeemAsync(first.Token)).Status.Should().Be(RefreshTokenStatus.Reused);
        (await refresh.RedeemAsync(rotated.Token.Token)).Status.Should().Be(RefreshTokenStatus.Invalid);
    }

    [Fact]
    public async Task Redeem_ShouldRejectTamperingExpiryAndARotatedStamp()
    {
        await using var host = CreateHost();
        var user = await host.CreateUserAsync("xena");
        await using var scope = host.Scope();
        var refresh = scope.ServiceProvider.GetRequiredService<RefreshTokenService>();
        var accounts = scope.ServiceProvider.GetRequiredService<UserAccountService>();

        var token = (await refresh.IssueAsync(user)).Token;
        (await refresh.RedeemAsync(token[..^3] + "AAA")).Status.Should().Be(RefreshTokenStatus.Invalid);
        (await refresh.RedeemAsync("garbage")).Status.Should().Be(RefreshTokenStatus.Invalid);

        host.Time.Advance(TimeSpan.FromDays(7));
        (await refresh.RedeemAsync(token)).Status.Should().Be(RefreshTokenStatus.Expired);

        var live = (await refresh.IssueAsync(user)).Token;
        await accounts.RotateSecurityStampAsync((await accounts.FindByIdAsync(user.Id))!);
        (await refresh.RedeemAsync(live)).Status.Should().Be(RefreshTokenStatus.Invalid);
        (await refresh.PurgeExpiredAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Revoke_ShouldEndOneFamilyAndRevokeAllShouldEndEvery()
    {
        await using var host = CreateHost();
        var user = await host.CreateUserAsync("yuri");
        await using var scope = host.Scope();
        var refresh = scope.ServiceProvider.GetRequiredService<RefreshTokenService>();

        var phone = (await refresh.IssueAsync(user)).Token;
        var laptop = (await refresh.IssueAsync(user)).Token;
        await refresh.RevokeAsync(phone);
        (await refresh.RedeemAsync(phone)).Status.Should().Be(RefreshTokenStatus.Invalid);

        var next = await refresh.RedeemAsync(laptop);
        next.IsSuccess.Should().BeTrue();
        await refresh.RevokeAllAsync(user);
        (await refresh.RedeemAsync(next.Token!.Token)).Status.Should().Be(RefreshTokenStatus.Invalid);
    }

    [Fact]
    public async Task Family_ShouldEndAsAFixedSession_AtTheDefaultOrTheExtendedLifetime()
    {
        await using var host = AccountsHost.Create(identity => identity.AddSessions().AddRefreshTokens());
        var user = await host.CreateUserAsync("yuri");
        await using var scope = host.Scope();
        var refresh = scope.ServiceProvider.GetRequiredService<RefreshTokenService>();
        var signedInAt = host.Time.GetUtcNow();

        var brief = await refresh.IssueAsync(user);
        brief.ExpiresAt.Should().Be(signedInAt.AddHours(12));
        (await refresh.IssueAsync(user, isPersistent: true)).ExpiresAt.Should().Be(signedInAt.AddDays(30));

        host.Time.Advance(TimeSpan.FromHours(1));
        var rotated = await refresh.RedeemAsync(brief.Token);
        rotated.Token!.ExpiresAt.Should().Be(brief.ExpiresAt, "a rotation keeps a fixed family's end");
    }

    [Fact]
    public async Task Family_ShouldSlide_WithEachRotation_WhileSessionsSlide()
    {
        await using var host = AccountsHost.Create(identity => identity
            .AddSessions(o =>
            {
                o.Expiration = SessionExpiration.Sliding;
                o.SlidingLifetime = TimeSpan.FromDays(5);
            })
            .AddRefreshTokens());
        var user = await host.CreateUserAsync("zara");
        await using var scope = host.Scope();
        var refresh = scope.ServiceProvider.GetRequiredService<RefreshTokenService>();

        var first = await refresh.IssueAsync(user, isPersistent: true);
        first.ExpiresAt.Should().Be(host.Time.GetUtcNow().AddDays(5), "a sliding family ignores the ask to stay");

        host.Time.Advance(TimeSpan.FromDays(3));
        var rotated = await refresh.RedeemAsync(first.Token);
        rotated.Token!.ExpiresAt.Should().Be(host.Time.GetUtcNow().AddDays(5));

        host.Time.Advance(TimeSpan.FromDays(6));
        (await refresh.RedeemAsync(rotated.Token.Token)).Status.Should().Be(RefreshTokenStatus.Expired);
    }
}
