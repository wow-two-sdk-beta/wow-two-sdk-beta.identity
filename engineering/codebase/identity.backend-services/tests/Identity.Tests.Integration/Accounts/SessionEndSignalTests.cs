using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.AccessChanges;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Emails;
using WoW.Two.Sdk.Beta.Be.Identity.Core.ExternalAccounts;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Lockout;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PersonalData;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Phones;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Signals;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Tokens;
using WoW.Two.Sdk.Beta.Be.Identity.Core.TwoFactor;
using WoW.Two.Sdk.Beta.Be.Identity.Core.UserClaims;
using WoW.Two.Sdk.Beta.Be.Identity.Mfa.Totp;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;
using WoW.Two.Sdk.Beta.Be.Identity.Otp.Models;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Accounts;

/// <summary>
/// What ends a session says so at once, so a service holding its token hears: every stamp rotation and an account's deletion
/// tell the handlers of each session and membership, with no later request needed. A password change still keeps its own session.
/// </summary>
public sealed class SessionEndSignalTests
{
    private const string Password = "correct horse battery";
    private const string Replacement = "a brand new passphrase";

    [Fact]
    public async Task EveryStampRotation_ShouldEndThePersonsSessions_AtOnce_AndTellTheHandlers()
    {
        var log = new Log();
        var sms = new SmsChannel();
        await using var host = CreateHost(log, sms);
        var rotations = new Dictionary<string, (Func<IServiceProvider, IdentityUser, Task> Arrange, Func<IServiceProvider, IdentityUser, Task<IdentityResult>> Rotate)>
        {
            ["a rotation asked for"] = (Nothing, (services, user) => services.GetRequiredService<UserAccountService>().RotateSecurityStampAsync(user)),
            ["a first password"] = (Nothing, (services, user) => services.GetRequiredService<UserPasswordService>().AddPasswordAsync(user, Password)),
            ["a changed password"] = (WithPassword, (services, user) => services.GetRequiredService<UserPasswordService>().ChangePasswordAsync(user, Password, Replacement)),
            ["a reset password"] = (WithPassword, (services, user) =>
            {
                var passwords = services.GetRequiredService<UserPasswordService>();
                return passwords.ResetPasswordAsync(user, passwords.IssuePasswordResetToken(user), Replacement);
            }),
            ["a removed password"] = (WithPassword, (services, user) => services.GetRequiredService<UserPasswordService>().RemovePasswordAsync(user)),
            ["a changed email"] = (Nothing, (services, user) =>
            {
                var emails = services.GetRequiredService<UserEmailService>();
                var address = $"moved-{user.Id:N}@example.test";
                return emails.ChangeEmailAsync(user, address, emails.IssueChangeEmailToken(user, address));
            }),
            ["a set email"] = (Nothing, (services, user) => services.GetRequiredService<UserEmailService>().SetEmailAsync(user, $"set-{user.Id:N}@example.test")),
            ["a set phone"] = (Nothing, (services, user) => services.GetRequiredService<UserPhoneService>().SetPhoneNumberAsync(user, "+998901234567")),
            ["a reset authenticator"] = (Nothing, async (services, user) =>
            {
                await services.GetRequiredService<UserTwoFactorService>().ResetAuthenticatorKeyAsync(user);
                return IdentityResult.Success;
            }),
            ["two-factor enabled"] = (WithAuthenticator, (services, user) => EnableAuthenticatorAsync(services, user)),
            ["two-factor disabled"] = (WithTwoFactor, (services, user) => services.GetRequiredService<UserTwoFactorService>().DisableAsync(user)),
            ["two-factor enabled by a delivered code"] = (WithConfirmedPhone, async (services, user) =>
            {
                var methods = services.GetRequiredService<UserTwoFactorMethodService>();
                await methods.SendCodeAsync(user, "sms");
                return await methods.EnableAsync(user, "sms", sms.LastCode);
            }),
            ["an unlinked provider"] = (WithProvider, (services, user) => services.GetRequiredService<ExternalAccountService>().UnlinkAsync(user, "github", user.Id.ToString("N"))),
            ["a removed claim"] = (WithClaim, (services, user) => services.GetRequiredService<UserClaimService>().RemoveClaimAsync(user, new Claim("plan", "free"))),
            ["a replaced claim"] = (WithClaim, (services, user) => services.GetRequiredService<UserClaimService>().ReplaceClaimAsync(user, new Claim("plan", "free"), new Claim("plan", "paid"))),
        };

        foreach (var (reason, (arrange, rotate)) in rotations)
        {
            var person = await host.CreateUserAsync($"p{Guid.NewGuid():N}"[..12]);
            await WithPersonAsync(host, person.Id, arrange);
            var laptop = await StartAsync(host, person.Id);
            var phone = await StartAsync(host, person.Id);
            log.Revoked.Clear();

            await WithPersonAsync(host, person.Id, async (services, user) => (await rotate(services, user)).IsSuccess.Should().BeTrue(reason));

            log.Revoked.Should().BeEquivalentTo([laptop.Id, phone.Id], reason);
            (await RowAsync(host, laptop.Id))!.RevokeReason.Should().Be(SessionRevokeReasonConstants.SecurityStamp, reason);
        }
    }

    [Fact]
    public async Task PasswordChange_ShouldKeepTheSessionItWasMadeIn_AndTellOnlyOfTheOthers()
    {
        var log = new Log();
        await using var host = CreateHost(log, new SmsChannel());
        var person = await host.CreateUserAsync("keeper");
        await WithPersonAsync(host, person.Id, WithPassword);
        var here = await StartAsync(host, person.Id);
        var elsewhere = await StartAsync(host, person.Id);

        await WithPersonAsync(host, person.Id, async (services, user) =>
            (await services.GetRequiredService<UserPasswordChangeService>().ChangeAsync(user, Password, Replacement, code: null, isTicketed: false, here.Id)).IsSessionKept.Should().BeTrue());

        log.Revoked.Should().Equal(elsewhere.Id);
        await using var scope = host.Scope();
        (await scope.ServiceProvider.GetRequiredService<UserSessionService>().ValidateAsync(here.Id, person.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task Erasure_ShouldTellOfEverySessionAndMembership()
    {
        var log = new Log();
        await using var host = CreateHost(log, new SmsChannel());
        var person = await host.CreateUserAsync("erased");
        var first = await CreateOrganizationAsync(host, person.Id, "first");
        var second = await CreateOrganizationAsync(host, person.Id, "second");
        var laptop = await StartAsync(host, person.Id);
        var phone = await StartAsync(host, person.Id);
        log.Changes.Clear();

        await WithPersonAsync(host, person.Id, (services, user) => services.GetRequiredService<UserPersonalDataService>().DeleteAsync(user));

        log.Revoked.Should().BeEquivalentTo([laptop.Id, phone.Id], "what they issued must stop counting now");
        log.Changes.Select(change => change.OrganizationId).Should().BeEquivalentTo(first.ToString("D"), second.ToString("D"));
        log.Changes.Should().OnlyContain(change => change.Kind == AccessChangeKind.Removed && change.Subject == person.Id.ToString("D"), "a job tracks access lost by these");
        (await RowAsync(host, laptop.Id)).Should().BeNull("the erasure still leaves no row");
    }

    [Fact]
    public async Task Erasure_ShouldTellNobody_WhenItRunsAgain()
    {
        var log = new Log();
        await using var host = CreateHost(log, new SmsChannel());
        var person = await host.CreateUserAsync("twice");
        await CreateOrganizationAsync(host, person.Id, "only");
        await StartAsync(host, person.Id);
        await WithPersonAsync(host, person.Id, (services, user) => services.GetRequiredService<UserPersonalDataService>().DeleteAsync(user));
        log.Changes.Clear();
        log.Revoked.Clear();

        await using var scope = host.Scope();
        await scope.ServiceProvider.GetRequiredService<UserPersonalDataService>().DeleteAsync(new IdentityUser { Id = person.Id });

        log.Revoked.Should().BeEmpty();
        log.Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task SoftDelete_ShouldEndThePersonsSessions_AtOnce()
    {
        var log = new Log();
        await using var host = CreateHost(log, new SmsChannel());
        var person = await host.CreateUserAsync("soft");
        var laptop = await StartAsync(host, person.Id);

        await WithPersonAsync(host, person.Id, (services, user) => services.GetRequiredService<UserAccountService>().DeleteAsync(user));

        log.Revoked.Should().Equal(laptop.Id);
    }

    private static AccountsHost CreateHost(Log log, SmsChannel sms) => AccountsHost.Create(
        identity => identity
            .AddArgon2Passwords()
            .AddUserTokens(o => o.SigningKey = AccountsHost.SigningKey)
            .AddLockout()
            .AddEmailConfirmation()
            .AddPhoneNumbers()
            .AddTwoFactor(o =>
            {
                o.Issuer = "Tests";
                o.Methods["sms"] = new TwoFactorMethodOptions { Channel = OtpChannelNameConstants.Sms };
            })
            .AddExternalAccounts()
            .AddUserClaims()
            .AddSessions()
            .AddPasswordChange()
            .AddOrganizations(o => o.Roles[OrganizationRoleConstants.Editor] = ["content:edit"])
            .AddPersonalData()
            .AddSignIn(),
        services: services =>
        {
            services.AddOtpService(o => o.Messages.AppName = "Tests");
            services.AddKeyedSingleton<IOtpDeliveryHandler>(OtpChannelNameConstants.Sms, sms);
            services.AddScoped<ISessionRevocationHandler>(_ => log);
            services.AddScoped<IAccessChangeHandler>(_ => log);
        });

    private static Task Nothing(IServiceProvider services, IdentityUser user) => Task.CompletedTask;

    private static Task WithPassword(IServiceProvider services, IdentityUser user)
        => services.GetRequiredService<UserPasswordService>().AddPasswordAsync(user, Password);

    private static Task WithAuthenticator(IServiceProvider services, IdentityUser user)
        => services.GetRequiredService<UserTwoFactorService>().ResetAuthenticatorKeyAsync(user);

    private static async Task WithTwoFactor(IServiceProvider services, IdentityUser user)
    {
        await WithAuthenticator(services, user);
        (await EnableAuthenticatorAsync(services, user)).IsSuccess.Should().BeTrue();
    }

    private static Task WithConfirmedPhone(IServiceProvider services, IdentityUser user)
    {
        user.PhoneNumber = "+998901234567";
        user.IsPhoneNumberConfirmed = true;
        return services.GetRequiredService<UserAccountService>().UpdateAsync(user);
    }

    private static Task WithProvider(IServiceProvider services, IdentityUser user)
        => services.GetRequiredService<ExternalAccountService>().LinkAsync(user, "github", user.Id.ToString("N"));

    private static Task WithClaim(IServiceProvider services, IdentityUser user)
        => services.GetRequiredService<UserClaimService>().AddClaimsAsync(user, [new Claim("plan", "free")]);

    private static async Task<IdentityResult> EnableAuthenticatorAsync(IServiceProvider services, IdentityUser user)
    {
        var twoFactor = services.GetRequiredService<UserTwoFactorService>();
        var key = Base32Encoding.ToBytes((await twoFactor.GetAuthenticatorKeyAsync(user))!);
        return await twoFactor.EnableAsync(user, services.GetRequiredService<ITotpService>().ComputeCode(key));
    }

    /// <summary>Runs <paramref name="action"/> on the person, read in a fresh scope as a request would.</summary>
    private static async Task WithPersonAsync(AccountsHost host, Guid userId, Func<IServiceProvider, IdentityUser, Task> action)
    {
        await using var scope = host.Scope();
        var person = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(userId))!;
        await action(scope.ServiceProvider, person);
    }

    private static async Task<IdentitySession> StartAsync(AccountsHost host, Guid userId)
    {
        await using var scope = host.Scope();
        return await scope.ServiceProvider.GetRequiredService<UserSessionService>().StartAsync(await host.ReloadAsync(userId), ["pwd"]);
    }

    private static async Task<IdentitySession?> RowAsync(AccountsHost host, Guid id)
    {
        await using var scope = host.Scope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Sessions.AsNoTracking().SingleOrDefaultAsync(session => session.Id == id);
    }

    private static async Task<Guid> CreateOrganizationAsync(AccountsHost host, Guid ownerId, string slug)
    {
        await using var scope = host.Scope();
        var organization = new IdentityOrganization { Name = slug, Slug = slug };
        var owner = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(ownerId))!;
        (await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateAsync(organization, owner)).IsSuccess.Should().BeTrue();
        return organization.Id;
    }

    private sealed class Log : IAccessChangeHandler, ISessionRevocationHandler
    {
        public List<AccessChangedEvent> Changes { get; } = [];

        public List<Guid> Revoked { get; } = [];

        public Task HandleAsync(AccessChangedEvent change, CancellationToken cancellationToken = default)
        {
            Changes.Add(change);
            return Task.CompletedTask;
        }

        public Task HandleAsync(IReadOnlyList<IdentitySession> sessions, CancellationToken cancellationToken = default)
        {
            Revoked.AddRange(sessions.Select(session => session.Id));
            return Task.CompletedTask;
        }
    }

    private sealed class SmsChannel : IOtpDeliveryHandler
    {
        public string LastCode { get; private set; } = string.Empty;

        public Task<OtpDeliveryResult> SendAsync(OtpDeliveryEnvelopeModel envelope, CancellationToken cancellationToken = default)
        {
            LastCode = envelope.Code;
            return Task.FromResult(new OtpDeliveryResult { IsSuccess = true });
        }
    }
}
