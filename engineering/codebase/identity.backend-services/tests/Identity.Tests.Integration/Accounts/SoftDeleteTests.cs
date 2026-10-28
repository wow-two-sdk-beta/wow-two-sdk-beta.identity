using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Logins;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PersonalData;
using WoW.Two.Sdk.Beta.Be.Identity.Core.RefreshTokens;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignIn;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Accounts;

/// <summary>
/// Deleting an account keeps its row with <c>deleted_at</c> set, and erasing it scrubs the row in place: neither is ever
/// found again by a lookup, a sign-in or a token, and the user name and email become free for a new account.
/// </summary>
public sealed class SoftDeleteTests
{
    private const string Password = "correct horse battery";

    private static AccountsHost CreateHost() => AccountsHost.Create(
        identity => identity
            .AddArgon2Passwords()
            .AddExternalLogins()
            .AddRefreshTokens(o => o.Lifetime = TimeSpan.FromDays(7))
            .AddPersonalData()
            .AddSignIn(),
        options => options.User.RequireUniqueEmail = true);

    [Fact]
    public async Task DeletingAUser_ShouldKeepTheRow_AndHideItFromLookupsAndSignIn()
    {
        await using var host = CreateHost();
        var user = await CreateWithPasswordAsync(host, "dana");
        var deletedAt = host.Time.GetUtcNow();
        await using (var scope = host.Scope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<UserAccountService>();
            await scope.ServiceProvider.GetRequiredService<UserLoginService>().AddLoginAsync((await accounts.FindByIdAsync(user.Id))!, "GitHub", "gh-dana");

            (await accounts.DeleteAsync((await accounts.FindByIdAsync(user.Id))!)).Succeeded.Should().BeTrue();

            (await accounts.FindByIdAsync(user.Id)).Should().BeNull("the deleted user leaves the scope that deleted it");
        }

        host.Time.Advance(TimeSpan.FromMinutes(1));
        await using var next = host.Scope();
        var services = next.ServiceProvider;
        var lookups = services.GetRequiredService<UserAccountService>();
        (await lookups.FindByIdAsync(user.Id)).Should().BeNull();
        (await lookups.FindByNameAsync("DANA")).Should().BeNull();
        (await lookups.FindByEmailAsync("dana@example.test")).Should().BeNull();
        (await services.GetRequiredService<UserLoginService>().FindByLoginAsync("GitHub", "gh-dana")).Should().BeNull();
        var signIn = services.GetRequiredService<SignInService>();
        (await signIn.PasswordSignInAsync("dana", Password)).Status.Should().Be(SignInStatus.Failed);
        (await signIn.PasswordSignInAsync("dana@example.test", Password)).Status.Should().Be(SignInStatus.Failed);

        var row = await services.GetRequiredService<IdentityDbContext>().Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(u => u.Id == user.Id);
        row.DeletedAt.Should().Be(deletedAt);
        row.UserName.Should().Be("dana", "a soft delete keeps the row as it was; erasure scrubs it");
        (await services.GetRequiredService<IdentityDbContext>().Set<IdentityUserLogin>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RefreshTokens_ShouldNotRedeem_ForADeletedUser()
    {
        await using var host = CreateHost();
        var user = await CreateWithPasswordAsync(host, "erin");
        string token;
        await using (var scope = host.Scope())
            token = (await scope.ServiceProvider.GetRequiredService<RefreshTokenService>().IssueAsync(user)).Token;

        // The row deleted under the token: the refresh tokens a delete removes are still in place here.
        await using (var scope = host.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users
                .Where(u => u.Id == user.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.DeletedAt, host.Time.GetUtcNow()));
        }

        await using var next = host.Scope();
        var redeemed = await next.ServiceProvider.GetRequiredService<RefreshTokenService>().RedeemAsync(token);

        redeemed.Status.Should().Be(RefreshTokenStatus.Invalid);
        redeemed.User.Should().BeNull();
    }

    [Fact]
    public async Task ADeletedUsersNameAndEmail_ShouldBeFreeForANewAccount()
    {
        await using var host = CreateHost();
        var first = await CreateWithPasswordAsync(host, "farah");
        await using (var scope = host.Scope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<UserAccountService>();
            (await accounts.DeleteAsync((await accounts.FindByIdAsync(first.Id))!)).Succeeded.Should().BeTrue();
        }

        var second = await CreateWithPasswordAsync(host, "FARAH", "Farah@Example.Test");

        await using var next = host.Scope();
        var lookups = next.ServiceProvider.GetRequiredService<UserAccountService>();
        (await lookups.FindByNameAsync("farah"))!.Id.Should().Be(second.Id);
        (await lookups.FindByEmailAsync("farah@example.test"))!.Id.Should().Be(second.Id);
        (await next.ServiceProvider.GetRequiredService<SignInService>().PasswordSignInAsync("farah", Password)).UserId.Should().Be(second.Id.ToString());
        (await next.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.IgnoreQueryFilters().CountAsync(u => u.NormalizedUserName == "FARAH")).Should().Be(2);
    }

    [Fact]
    public async Task ErasingAnAccount_ShouldScrubThePersonalColumns_AndKeepTheRow()
    {
        await using var host = CreateHost();
        var user = await CreateWithPasswordAsync(host, "gulya");
        var erasedAt = host.Time.GetUtcNow();
        await using (var scope = host.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<RefreshTokenService>().IssueAsync(user);
            var accounts = scope.ServiceProvider.GetRequiredService<UserAccountService>();
            await scope.ServiceProvider.GetRequiredService<UserPersonalDataService>().DeleteAsync((await accounts.FindByIdAsync(user.Id))!);

            (await accounts.FindByIdAsync(user.Id)).Should().BeNull("the erased user leaves the scope that erased it");
        }

        await using var next = host.Scope();
        var context = next.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var row = await context.Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(u => u.Id == user.Id);
        row.Should().BeEquivalentTo(new
        {
            UserName = (string?)null,
            NormalizedUserName = (string?)null,
            Email = (string?)null,
            NormalizedEmail = (string?)null,
            EmailConfirmed = false,
            PasswordHash = (string?)null,
            PhoneNumber = (string?)null,
            PhoneNumberConfirmed = false,
            TwoFactorEnabled = false,
            DeletedAt = (DateTimeOffset?)erasedAt,
        }, options => options.ExcludingMissingMembers());
        row.SecurityStamp.Should().NotBe(user.SecurityStamp);
        (await context.Set<IdentityRefreshToken>().CountAsync()).Should().Be(0);
        (await next.ServiceProvider.GetRequiredService<SignInService>().PasswordSignInAsync("gulya", Password)).Status.Should().Be(SignInStatus.Failed);
    }

    [Fact]
    public async Task ErasingASoftDeletedAccount_ShouldScrubIt_AndKeepItsDeletionTime()
    {
        await using var host = CreateHost();
        var user = await CreateWithPasswordAsync(host, "hamid");
        var deletedAt = host.Time.GetUtcNow();
        IdentityUser deleted;
        await using (var scope = host.Scope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<UserAccountService>();
            deleted = (await accounts.FindByIdAsync(user.Id))!;
            await accounts.DeleteAsync(deleted);
        }

        host.Time.Advance(TimeSpan.FromDays(30));
        await using (var scope = host.Scope())
        {
            var personalData = scope.ServiceProvider.GetRequiredService<UserPersonalDataService>();
            await personalData.DeleteAsync(deleted);
            await personalData.DeleteAsync(deleted);
        }

        await using var next = host.Scope();
        var row = await next.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(u => u.Id == user.Id);
        row.UserName.Should().BeNull();
        row.Email.Should().BeNull();
        row.DeletedAt.Should().Be(deletedAt, "erasure keeps the time the account was deleted");
        row.UpdatedAt.Should().Be(host.Time.GetUtcNow());
    }

    private static async Task<IdentityUser> CreateWithPasswordAsync(AccountsHost host, string userName, string? email = null)
    {
        await using var scope = host.Scope();
        var user = new IdentityUser { UserName = userName, Email = email ?? $"{userName}@example.test" };
        var created = await scope.ServiceProvider.GetRequiredService<UserPasswordService>().CreateWithPasswordAsync(user, Password);
        created.Succeeded.Should().BeTrue();
        return user;
    }
}
