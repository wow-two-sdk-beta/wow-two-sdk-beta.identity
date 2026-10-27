using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Accounts;

/// <summary>Sign-in sessions (I16): start, validate, list, revoke one or all, and the stamp rotation that ends them all.</summary>
public sealed class SessionTests
{
    private static AccountsHost CreateHost(RevocationLog? revocations = null) => AccountsHost.Create(
        identity => identity.AddSessions(o => o.ActivityInterval = TimeSpan.FromMinutes(5)),
        services: services =>
        {
            if (revocations is not null)
                services.AddScoped<ISessionRevocationHandler>(_ => revocations);
        });

    [Fact]
    public async Task Session_ShouldLiveUntilItExpires_AndRecordItsUseAtMostPerInterval()
    {
        await using var host = CreateHost();
        var user = await host.CreateUserAsync("abby");
        var started = host.Time.GetUtcNow();
        var session = await StartAsync(host, user, "pwd", "mfa");

        session.Id.Version.Should().Be(7);
        session.ExpiresAt.Should().Be(started.AddHours(12));
        (await ValidateAsync(host, session.Id, user.Id)).Should().BeTrue();
        (await ValidateAsync(host, session.Id, Guid.CreateVersion7())).Should().BeFalse("a session belongs to one person");

        host.Time.Advance(TimeSpan.FromMinutes(2));
        await ValidateAsync(host, session.Id, user.Id);
        (await RowAsync(host, session.Id)).LastSeenAt.Should().Be(started, "two minutes is inside the activity interval");
        host.Time.Advance(TimeSpan.FromMinutes(4));
        await ValidateAsync(host, session.Id, user.Id);
        (await RowAsync(host, session.Id)).LastSeenAt.Should().Be(host.Time.GetUtcNow());

        host.Time.Advance(TimeSpan.FromHours(12));
        (await ValidateAsync(host, session.Id, user.Id)).Should().BeFalse("the lifetime is absolute");
    }

    [Fact]
    public async Task Person_ShouldListAndRevokeOneOrAllSessions()
    {
        var revocations = new RevocationLog();
        await using var host = CreateHost(revocations);
        var user = await host.CreateUserAsync("bram");
        var laptop = await StartAsync(host, user, "pwd");
        host.Time.Advance(TimeSpan.FromMinutes(1));
        var phone = await StartAsync(host, user, "hwk");
        host.Time.Advance(TimeSpan.FromMinutes(1));
        var tablet = await StartAsync(host, user, "pwd");
        var stranger = await host.CreateUserAsync("cleo");

        await using var scope = host.Scope();
        var sessions = scope.ServiceProvider.GetRequiredService<UserSessionService>();
        (await sessions.ListAsync(user)).Select(s => s.Id).Should().Equal(tablet.Id, phone.Id, laptop.Id);
        (await sessions.RevokeAsync(stranger.Id, phone.Id)).Should().BeFalse("only the owner revokes a session");

        (await sessions.RevokeAsync(user.Id, phone.Id)).Should().BeTrue();
        (await sessions.RevokeAsync(user.Id, phone.Id)).Should().BeFalse("an ended session stays ended");
        (await ValidateAsync(host, phone.Id, user.Id)).Should().BeFalse();
        (await sessions.ListAsync(user)).Select(s => s.Id).Should().Equal(tablet.Id, laptop.Id);

        (await sessions.RevokeAllAsync(user, keep: tablet.Id)).Should().Be(1);
        (await sessions.ListAsync(user)).Select(s => s.Id).Should().Equal(tablet.Id);
        (await sessions.RevokeAllAsync(user)).Should().Be(1);
        (await sessions.ListAsync(user)).Should().BeEmpty();

        (await RowAsync(host, phone.Id)).RevokeReason.Should().Be(SessionRevokeReasonConstants.Revoked);
        (await RowAsync(host, laptop.Id)).RevokeReason.Should().Be(SessionRevokeReasonConstants.RevokedAll);
        revocations.Revoked.Should().Equal(phone.Id, laptop.Id, tablet.Id);
    }

    [Fact]
    public async Task StampRotation_ShouldEndEverySessionOfThePerson()
    {
        var revocations = new RevocationLog();
        await using var host = CreateHost(revocations);
        var user = await host.CreateUserAsync("dina");
        var first = await StartAsync(host, user, "pwd");
        var second = await StartAsync(host, user, "pwd");

        await using (var scope = host.Scope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<UserAccountService>();
            await accounts.RotateSecurityStampAsync((await accounts.FindByIdAsync(user.Id))!);
        }

        (await ValidateAsync(host, first.Id, user.Id)).Should().BeFalse();
        await using (var scope = host.Scope())
            (await scope.ServiceProvider.GetRequiredService<UserSessionService>().ListAsync(await host.ReloadAsync(user.Id))).Should().BeEmpty();

        (await RowAsync(host, first.Id)).RevokeReason.Should().Be(SessionRevokeReasonConstants.SecurityStamp);
        (await RowAsync(host, second.Id)).RevokeReason.Should().Be(SessionRevokeReasonConstants.SecurityStamp);
        revocations.Revoked.Should().BeEquivalentTo([first.Id, second.Id], "what the sessions issued ends with them");
    }

    private static async Task<IdentitySession> StartAsync(AccountsHost host, IdentityUser user, params string[] methods)
    {
        await using var scope = host.Scope();
        return await scope.ServiceProvider.GetRequiredService<UserSessionService>().StartAsync(await host.ReloadAsync(user.Id), methods);
    }

    private static async Task<bool> ValidateAsync(AccountsHost host, Guid sessionId, Guid userId)
    {
        await using var scope = host.Scope();
        return await scope.ServiceProvider.GetRequiredService<UserSessionService>().ValidateAsync(sessionId, userId);
    }

    private static async Task<IdentitySession> RowAsync(AccountsHost host, Guid id)
    {
        await using var scope = host.Scope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Sessions.AsNoTracking().SingleAsync(s => s.Id == id);
    }

    /// <summary>Records which sessions the revocation handlers were told about, in order.</summary>
    internal sealed class RevocationLog : ISessionRevocationHandler
    {
        public List<Guid> Revoked { get; } = [];

        public Task HandleAsync(IReadOnlyList<IdentitySession> sessions, CancellationToken cancellationToken = default)
        {
            lock (Revoked)
                Revoked.AddRange(sessions.OrderBy(session => session.CreatedAt).ThenBy(session => session.Id).Select(session => session.Id));
            return Task.CompletedTask;
        }
    }
}
