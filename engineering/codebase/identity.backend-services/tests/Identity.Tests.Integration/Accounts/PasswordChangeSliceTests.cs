using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Lockout;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Tokens;
using WoW.Two.Sdk.Beta.Be.Identity.Core.TwoFactor;
using WoW.Two.Sdk.Beta.Be.Identity.Mfa.Totp;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Accounts;

/// <summary>The password change slice below the HTTP API: the forced change at sign-in with a second factor, and what a change does to sessions.</summary>
public sealed class PasswordChangeSliceTests
{
    private const string OldPassword = "old-password!";
    private const string NewPassword = "a brand new passphrase";

    private static AccountsHost CreateHost(Action<IdentityCoreOptions>? options = null) => AccountsHost.Create(
        identity => identity
            .AddArgon2Passwords()
            .AddUserTokens(o => o.SigningKey = AccountsHost.SigningKey)
            .AddLockout()
            .AddSessions()
            .AddTwoFactor(o => o.Issuer = "Tests")
            .AddPasswordChange()
            .AddSignIn(),
        options);

    [Fact]
    public async Task SignIn_ShouldForceTheChangeOnlyAfterTheSecondFactor_WhenThePasswordBreaksTheCurrentRules()
    {
        await using var host = CreateHost(options => options.Password.MinLength = 8);
        await using var scope = host.Scope();
        var services = scope.ServiceProvider;
        var twoFactor = services.GetRequiredService<UserTwoFactorService>();
        var signIn = services.GetRequiredService<SignInService>();
        var totp = services.GetRequiredService<ITotpService>();
        var user = new IdentityUser { UserName = "rita", Email = "rita@example.test" };
        (await services.GetRequiredService<UserPasswordService>().CreateWithPasswordAsync(user, OldPassword)).IsSuccess.Should().BeTrue();
        var key = Base32Encoding.ToBytes(await twoFactor.ResetAuthenticatorKeyAsync(user));
        (await twoFactor.EnableAsync(user, totp.ComputeCode(key))).IsSuccess.Should().BeTrue();

        // The rule tightens after the password was stored.
        host.Provider.GetRequiredService<IdentityCoreOptions>().Password.MinLength = 15;

        var first = await signIn.PasswordSignInAsync("rita", OldPassword);

        first.Status.Should().Be(SignInStatus.RequiresTwoFactor, "the second factor comes before anything about the password is revealed");
        first.IsPasswordChangeRequired.Should().BeTrue();
        first.PasswordChangeTicket.Should().BeNull();
        var wrong = await signIn.TwoFactorSignInAsync(user.Id, first.TwoFactorTicket!, "000000");
        (await signIn.FinishPasswordSignInAsync(first, wrong)).Status.Should().Be(SignInStatus.Failed);

        var completed = await signIn.TwoFactorSignInAsync(user.Id, first.TwoFactorTicket!, totp.ComputeCode(key));
        completed.IsSuccess.Should().BeTrue();
        var final = await signIn.FinishPasswordSignInAsync(first, completed);

        final.Status.Should().Be(SignInStatus.PasswordChangeRequired);
        final.Principal.Should().BeNull();
        final.UserId.Should().Be(user.Id.ToString());
        (await services.GetRequiredService<UserPasswordChangeService>().RedeemRequiredChangeTicketAsync(final.PasswordChangeTicket))!.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task SignIn_ShouldCompleteAsBefore_WhenThePasswordMeetsTheCurrentRules()
    {
        await using var host = CreateHost();
        await using var scope = host.Scope();
        var signIn = scope.ServiceProvider.GetRequiredService<SignInService>();
        var user = new IdentityUser { UserName = "sol", Email = "sol@example.test" };
        await scope.ServiceProvider.GetRequiredService<UserPasswordService>().CreateWithPasswordAsync(user, NewPassword);

        var result = await signIn.PasswordSignInAsync("sol", NewPassword);

        result.Status.Should().Be(SignInStatus.Succeeded);
        result.IsPasswordChangeRequired.Should().BeFalse();
        (await signIn.FinishPasswordSignInAsync(result, result)).Should().BeSameAs(result);
    }

    [Fact]
    public async Task Ticket_ShouldDieWithTheChangeItAuthorized()
    {
        await using var host = CreateHost(options => options.Password.MinLength = 8);
        await using var scope = host.Scope();
        var services = scope.ServiceProvider;
        var change = services.GetRequiredService<UserPasswordChangeService>();
        var user = new IdentityUser { UserName = "tom", Email = "tom@example.test" };
        await services.GetRequiredService<UserPasswordService>().CreateWithPasswordAsync(user, OldPassword);
        host.Provider.GetRequiredService<IdentityCoreOptions>().Password.MinLength = 15;
        var ticket = (await services.GetRequiredService<SignInService>().PasswordSignInAsync("tom", OldPassword)).PasswordChangeTicket!;
        var holder = (await change.RedeemRequiredChangeTicketAsync(ticket))!;

        (await change.ChangeAsync(holder, currentPassword: null, NewPassword, code: null, isTicketed: true)).IsSuccess.Should().BeTrue();

        (await change.RedeemRequiredChangeTicketAsync(ticket)).Should().BeNull("the change rotated the stamp the ticket was bound to");
    }

    [Fact]
    public async Task Change_ShouldEndEveryOtherSession_AndKeepTheOneItWasMadeIn()
    {
        await using var host = CreateHost();
        await using var scope = host.Scope();
        var services = scope.ServiceProvider;
        var change = services.GetRequiredService<UserPasswordChangeService>();
        var sessions = services.GetRequiredService<UserSessionService>();
        var user = new IdentityUser { UserName = "una", Email = "una@example.test" };
        await services.GetRequiredService<UserPasswordService>().CreateWithPasswordAsync(user, NewPassword);
        var here = await sessions.StartAsync(user, ["pwd"]);
        var elsewhere = await sessions.StartAsync(user, ["pwd"]);

        var changed = await change.ChangeAsync(user, NewPassword, "yet another passphrase", code: null, isTicketed: false, here.Id);

        changed.IsSuccess.Should().BeTrue();
        changed.IsSessionKept.Should().BeTrue();
        (await sessions.ValidateAsync(here.Id, user.Id)).Should().BeTrue("the session the change was made in takes the new stamp");
        (await sessions.ValidateAsync(elsewhere.Id, user.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Change_ShouldEndEverySession_WhenSignInAgainIsRequired()
    {
        await using var host = CreateHost(options => options.Password.Change.IsSignInAgainRequired = true);
        await using var scope = host.Scope();
        var services = scope.ServiceProvider;
        var change = services.GetRequiredService<UserPasswordChangeService>();
        var sessions = services.GetRequiredService<UserSessionService>();
        var user = new IdentityUser { UserName = "vic", Email = "vic@example.test" };
        await services.GetRequiredService<UserPasswordService>().CreateWithPasswordAsync(user, NewPassword);
        var here = await sessions.StartAsync(user, ["pwd"]);

        var changed = await change.ChangeAsync(user, NewPassword, "yet another passphrase", code: null, isTicketed: false, here.Id);

        changed.IsSuccess.Should().BeTrue();
        changed.IsSessionKept.Should().BeFalse();
        (await sessions.ValidateAsync(here.Id, user.Id)).Should().BeFalse();
    }
}
