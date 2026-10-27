using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OtpNet;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Lockout;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignInLog;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Tokens;
using WoW.Two.Sdk.Beta.Be.Identity.Core.TwoFactor;
using WoW.Two.Sdk.Beta.Be.Identity.Mfa.Totp;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Accounts;

/// <summary>The sign-in log (I16): every attempt <c>SignInService</c> judges, masked, and the retention cleanup.</summary>
public sealed class SignInLogTests
{
    private const string Password = "correct horse battery";

    private static AccountsHost CreateHost(Action<SignInLogOptions>? log = null) => AccountsHost.Create(
        identity => identity
            .AddArgon2Passwords()
            .AddUserTokens(o => o.SigningKey = AccountsHost.SigningKey)
            .AddLockout()
            .AddTwoFactor(o => o.Issuer = "Tests")
            .AddSignIn()
            .AddSignInLog(log),
        options => options.Lockout.MaxFailedAttempts = 2);

    [Fact]
    public async Task PasswordAttempts_ShouldEachLeaveAMaskedRow()
    {
        await using var host = CreateHost();
        await using var scope = host.Scope();
        var user = new IdentityUser { UserName = "vera", Email = "vera@example.test" };
        await scope.ServiceProvider.GetRequiredService<UserPasswordService>().CreateWithPasswordAsync(user, Password);
        var signIn = scope.ServiceProvider.GetRequiredService<SignInService>();

        (await signIn.PasswordSignInAsync("nobody@example.test", Password)).Status.Should().Be(SignInStatus.Failed);
        (await signIn.PasswordSignInAsync("vera@example.test", "wrong")).Status.Should().Be(SignInStatus.Failed);
        (await signIn.PasswordSignInAsync("vera", "wrong")).Status.Should().Be(SignInStatus.LockedOut);
        (await signIn.PasswordSignInAsync("vera", Password)).Status.Should().Be(SignInStatus.LockedOut);
        host.Time.Advance(TimeSpan.FromMinutes(5));
        var success = await signIn.PasswordSignInAsync("vera@example.test", Password);

        success.SignInLogEntryId.Should().NotBeNull();
        var rows = await RowsAsync(host);
        rows.Select(row => (row.UserId, row.SubjectMasked, row.Method, row.Result, row.Reason)).Should().Equal(
            (null, "n***@example.test", SignInMethodConstants.Password, SignInResultConstants.Failed, SignInReasonConstants.UnknownAccount),
            (user.Id, "v***@example.test", SignInMethodConstants.Password, SignInResultConstants.Failed, SignInReasonConstants.WrongPassword),
            (user.Id, "ve***", SignInMethodConstants.Password, SignInResultConstants.LockedOut, SignInReasonConstants.LockoutStarted),
            (user.Id, "ve***", SignInMethodConstants.Password, SignInResultConstants.LockedOut, SignInReasonConstants.LockedOut),
            (user.Id, "v***@example.test", SignInMethodConstants.Password, SignInResultConstants.Succeeded, null));
        rows[^1].Id.Should().Be(success.SignInLogEntryId!.Value);
        rows.Should().OnlyContain(row => row.OccurredAt <= host.Time.GetUtcNow() && row.SessionId == null && row.IpMasked == null);
        (await scope.ServiceProvider.GetRequiredService<SignInLogService>().ListForUserAsync(user.Id, take: 2))
            .Select(row => row.Id).Should().Equal(rows[^1].Id, rows[^2].Id);
    }

    [Fact]
    public async Task SecondFactorAttempts_ShouldNameTheirMethod()
    {
        await using var host = CreateHost();
        await using var scope = host.Scope();
        var services = scope.ServiceProvider;
        var user = new IdentityUser { UserName = "wade", Email = "wade@example.test" };
        await services.GetRequiredService<UserPasswordService>().CreateWithPasswordAsync(user, Password);
        var twoFactor = services.GetRequiredService<UserTwoFactorService>();
        var totp = services.GetRequiredService<ITotpService>();
        var key = Base32Encoding.ToBytes(await twoFactor.ResetAuthenticatorKeyAsync(user));
        await twoFactor.EnableAsync(user, totp.ComputeCode(key));
        var recovery = await twoFactor.GenerateRecoveryCodesAsync(user);
        var signIn = services.GetRequiredService<SignInService>();

        var first = await signIn.PasswordSignInAsync("wade", Password);
        await signIn.TwoFactorSignInAsync(user.Id, "forged-ticket", totp.ComputeCode(key));
        await signIn.TwoFactorSignInAsync(user.Id, first.TwoFactorTicket!, "000000");
        var second = await signIn.PasswordSignInAsync("wade", Password);
        (await signIn.RecoveryCodeSignInAsync(user.Id, second.TwoFactorTicket!, recovery[0])).Succeeded.Should().BeTrue();

        (await RowsAsync(host)).Select(row => (row.Method, row.Result, row.Reason)).Should().Equal(
            (SignInMethodConstants.Password, SignInResultConstants.TwoFactorRequired, null),
            ("2fa:authenticator", SignInResultConstants.Failed, SignInReasonConstants.InvalidTicket),
            ("2fa:authenticator", SignInResultConstants.Failed, SignInReasonConstants.WrongCode),
            (SignInMethodConstants.Password, SignInResultConstants.TwoFactorRequired, null),
            (SignInMethodConstants.RecoveryCode, SignInResultConstants.Succeeded, null));
    }

    [Fact]
    public async Task Cleanup_ShouldDeleteOnlyRowsPastTheRetention_EvenWhenReplicasRunItTogether()
    {
        await using var host = CreateHost(o => o.RetentionDays = 30);
        var now = host.Time.GetUtcNow();
        await using (var seed = host.Scope())
        {
            var context = seed.ServiceProvider.GetRequiredService<IdentityDbContext>();
            context.SignInLog.AddRange(Enumerable.Range(0, 40).Select(day => new IdentitySignInLogEntry
            {
                OccurredAt = now.AddDays(-day).AddMinutes(-1),
                Method = SignInMethodConstants.Password,
                Result = SignInResultConstants.Failed,
            }));
            await context.SaveChangesAsync();
        }

        await using var first = host.Scope();
        await using var second = host.Scope();
        var deleted = await Task.WhenAll(
            first.ServiceProvider.GetRequiredService<SignInLogService>().DeleteExpiredAsync(),
            second.ServiceProvider.GetRequiredService<SignInLogService>().DeleteExpiredAsync());

        deleted.Sum().Should().Be(10, "days 30 to 39 lie past a 30-day retention, and each row goes once");
        (await RowsAsync(host)).Should().HaveCount(30).And.OnlyContain(row => row.OccurredAt >= now.AddDays(-30));
    }

    [Fact]
    public async Task CleanupJob_ShouldRunAtStartup()
    {
        await using var host = CreateHost();
        await using (var seed = host.Scope())
        {
            var context = seed.ServiceProvider.GetRequiredService<IdentityDbContext>();
            context.SignInLog.Add(new IdentitySignInLogEntry { OccurredAt = host.Time.GetUtcNow().AddDays(-181), Method = "password", Result = "failed" });
            context.SignInLog.Add(new IdentitySignInLogEntry { OccurredAt = host.Time.GetUtcNow().AddDays(-179), Method = "password", Result = "failed" });
            await context.SaveChangesAsync();
        }

        var job = host.Provider.GetServices<IHostedService>().OfType<BackgroundService>().Single();
        await job.StartAsync(CancellationToken.None);
        for (var wait = 0; wait < 100 && (await RowsAsync(host)).Count > 1; wait++)
            await Task.Delay(50);
        await job.StopAsync(CancellationToken.None);

        (await RowsAsync(host)).Should().ContainSingle().Which.OccurredAt.Should().Be(host.Time.GetUtcNow().AddDays(-179));
    }

    private static async Task<List<IdentitySignInLogEntry>> RowsAsync(AccountsHost host)
    {
        await using var scope = host.Scope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().SignInLog.AsNoTracking().OrderBy(row => row.Id).ToListAsync();
    }
}
