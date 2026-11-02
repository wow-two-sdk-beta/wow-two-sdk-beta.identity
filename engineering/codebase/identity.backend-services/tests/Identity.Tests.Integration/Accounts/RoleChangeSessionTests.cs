using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Invitations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.InviteSignUp;
using WoW.Two.Sdk.Beta.Be.Identity.Core.OneTimeCodes;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Core.ProfilePictures;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Roles;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;
using WoW.Two.Sdk.Beta.Be.Identity.Tests.PhoneSignIn;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Accounts;

/// <summary>
/// A role change signs the person out everywhere (<see cref="RoleChangeSignOut"/>): an instance role granted or removed,
/// an organization joined (a member added, an invitation redeemed, the invitation sign-up), a role changed, a member removed
/// or the organization deleted rotates the person's stamp and revokes their live sessions as <c>roles_changed</c>, telling
/// every revocation handler. Other people's sessions stay live, a call that changes nothing ends nothing, a registration
/// without sessions only rotates the stamp, and the invitation sign-up still signs the browser in after the join.
/// </summary>
public sealed class RoleChangeSessionTests
{
    private const string Phone = "+998901234567";
    private const string NewPhone = "+998907654321";
    private const long KnownChat = 7272;
    private const long NewChat = 7373;
    private const string HashKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    [Fact]
    public async Task InstanceRole_GrantAndRemoval_ShouldEachSignThePersonOut_AndLeaveOthersSignedIn()
    {
        var revocations = new SessionTests.RevocationLog();
        await using var host = CreateHost(revocations);
        var lola = await host.CreateUserAsync("lola");
        var sam = await host.CreateUserAsync("sam");
        var bystander = await StartAsync(host, sam.Id);
        var laptop = await StartAsync(host, lola.Id);
        var phone = await StartAsync(host, lola.Id);

        var stamp = await ChangeAsync(host, lola.Id, async (services, person) =>
            (await services.GetRequiredService<InstanceRoleService>().GrantAsync(person, InstanceRoleConstants.Admin)).Result);
        await ShouldBeSignedOutAsync(host, revocations, lola.Id, stamp, laptop, phone);
        await ShouldBeLiveAsync(host, bystander);

        var kept = await StartAsync(host, lola.Id);
        await using (var scope = host.Scope())
        {
            var person = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(lola.Id))!;
            (await scope.ServiceProvider.GetRequiredService<InstanceRoleService>().GrantAsync(person, InstanceRoleConstants.Admin)).IsGranted.Should().BeFalse();
        }

        await ShouldBeLiveAsync(host, kept, "a role already held changes nothing");

        stamp = await ChangeAsync(host, lola.Id, (services, person) =>
            services.GetRequiredService<UserRoleService>().RemoveFromRoleAsync(person, InstanceRoleConstants.Admin));
        await ShouldBeSignedOutAsync(host, revocations, lola.Id, stamp, kept);
        await ShouldBeLiveAsync(host, bystander);
        revocations.Revoked.Should().NotContain(bystander.Id);
    }

    [Fact]
    public async Task Joining_ByBeingAddedOrRedeemingAnInvitation_ShouldSignTheMemberOut_ButCreatingAnOrganizationShouldNot()
    {
        var revocations = new SessionTests.RevocationLog();
        await using var host = CreateHost(revocations);
        var owner = await host.CreateUserAsync("owner");
        var ownerSession = await StartAsync(host, owner.Id);
        var organization = await CreateOrganizationAsync(host, owner.Id);
        await ShouldBeLiveAsync(host, ownerSession, "the creator stays signed in");

        var ann = await host.CreateUserAsync("ann");
        var annSession = await StartAsync(host, ann.Id);
        var stamp = await ChangeAsync(host, ann.Id, (services, person) =>
            services.GetRequiredService<OrganizationService>().AddMemberAsync(organization, person, OrganizationRoleConstants.Editor));
        await ShouldBeSignedOutAsync(host, revocations, ann.Id, stamp, annSession);

        var ben = await host.CreateUserAsync("ben", "ben@example.test");
        var benSession = await StartAsync(host, ben.Id);
        var invitation = await InviteAsync(host, organization, owner.Id, email: "ben@example.test");
        stamp = await ChangeAsync(host, ben.Id, (services, person) => services.GetRequiredService<InvitationService>().RedeemAsync(invitation, person));
        await ShouldBeSignedOutAsync(host, revocations, ben.Id, stamp, benSession);

        var benAgain = await StartAsync(host, ben.Id);
        var second = await InviteAsync(host, organization, owner.Id, email: null);
        await ChangeAsync(host, ben.Id, (services, person) => services.GetRequiredService<InvitationService>().RedeemAsync(second, person));
        await ShouldBeLiveAsync(host, benAgain, "a member keeps their role, so nothing changed");
        await ShouldBeLiveAsync(host, ownerSession);
    }

    [Fact]
    public async Task RoleChange_Removal_AndDeletion_ShouldSignTheMembersOut()
    {
        var revocations = new SessionTests.RevocationLog();
        await using var host = CreateHost(revocations);
        var owner = await host.CreateUserAsync("owner");
        var ann = await host.CreateUserAsync("ann");
        var organization = await CreateOrganizationAsync(host, owner.Id);
        await AddAsync(host, organization, ann.Id);
        var ownerSession = await StartAsync(host, owner.Id);

        var first = await StartAsync(host, ann.Id);
        await ChangeAsync(host, ann.Id, (services, person) =>
            services.GetRequiredService<OrganizationService>().ChangeRoleAsync(organization, person, OrganizationRoleConstants.Editor));
        await ShouldBeLiveAsync(host, first, "the same role changes nothing");
        var stamp = await ChangeAsync(host, ann.Id, (services, person) =>
            services.GetRequiredService<OrganizationService>().ChangeRoleAsync(organization, person, OrganizationRoleConstants.Viewer));
        await ShouldBeSignedOutAsync(host, revocations, ann.Id, stamp, first);
        await ShouldBeLiveAsync(host, ownerSession, "the manager's roles did not change");

        var second = await StartAsync(host, ann.Id);
        stamp = await ChangeAsync(host, ann.Id, (services, person) =>
            services.GetRequiredService<OrganizationService>().RemoveMemberAsync(organization, person));
        await ShouldBeSignedOutAsync(host, revocations, ann.Id, stamp, second);

        await AddAsync(host, organization, ann.Id);
        var third = await StartAsync(host, ann.Id);
        var annStamp = (await host.ReloadAsync(ann.Id)).SecurityStamp;
        var ownerStamp = await ChangeAsync(host, owner.Id, async (services, _) =>
            await services.GetRequiredService<OrganizationService>().DeleteAsync(organization));
        await ShouldBeSignedOutAsync(host, revocations, ann.Id, annStamp, third);
        await ShouldBeSignedOutAsync(host, revocations, owner.Id, ownerStamp, ownerSession);
    }

    [Fact]
    public async Task WithoutSessions_ARoleChange_ShouldOnlyRotateTheStamp()
    {
        await using var host = AccountsHost.Create(identity => identity.AddRoles().AddOrganizations());
        var owner = await host.CreateUserAsync("owner");
        var lola = await host.CreateUserAsync("lola");
        var organization = await CreateOrganizationAsync(host, owner.Id);
        await using (var scope = host.Scope())
            scope.ServiceProvider.GetService<UserSessionService>().Should().BeNull();

        var stamp = await ChangeAsync(host, lola.Id, async (services, person) =>
            (await services.GetRequiredService<InstanceRoleService>().GrantAsync(person, InstanceRoleConstants.Support)).Result);
        (await host.ReloadAsync(lola.Id)).SecurityStamp.Should().NotBe(stamp);

        stamp = await ChangeAsync(host, lola.Id, (services, person) =>
            services.GetRequiredService<OrganizationService>().AddMemberAsync(organization, person, OrganizationRoleConstants.Viewer));
        (await host.ReloadAsync(lola.Id)).SecurityStamp.Should().NotBe(stamp);
    }

    [Fact]
    public async Task InviteSignUp_ShouldEndAKnownPersonsOldSessions_AndStillSignTheBrowserIn()
    {
        var revocations = new SessionTests.RevocationLog();
        var bot = new FakeTelegramBotClient();
        await using var host = CreateSignUpHost(bot, revocations);
        var owner = await host.CreateUserAsync("owner");
        var organization = await CreateOrganizationAsync(host, owner.Id);
        var known = await host.CreateUserAsync("lola");
        await using (var scope = host.Scope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<UserAccountService>();
            var tracked = (await accounts.FindByIdAsync(known.Id))!;
            tracked.PhoneNumber = Phone;
            tracked.IsPhoneNumberConfirmed = true;
            await accounts.UpdateAsync(tracked);
        }

        var oldSession = await StartAsync(host, known.Id);
        var stamp = (await host.ReloadAsync(known.Id)).SecurityStamp;
        var knownSignUp = await StartSignUpAsync(host, await TokenAsync(host, organization, owner.Id));
        await ShareAsync(host, knownSignUp, Phone, KnownChat);

        await ShouldBeSignedOutAsync(host, revocations, known.Id, stamp, oldSession);
        var knownBrowser = await SignInFromPollAsync(host, knownSignUp);
        knownBrowser.UserId.Should().Be(known.Id);
        await ShouldBeLiveAsync(host, knownBrowser, "the poll signs in after the join, under the new stamp");

        host.Time.Advance(TimeSpan.FromMinutes(1));
        var newSignUp = await StartSignUpAsync(host, await TokenAsync(host, organization, owner.Id));
        await ShareAsync(host, newSignUp, NewPhone, NewChat);
        var newBrowser = await SignInFromPollAsync(host, newSignUp);
        await ShouldBeLiveAsync(host, newBrowser, "a new person joins and signs in");
        await using (var scope = host.Scope())
        {
            var person = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(newBrowser.UserId))!;
            (await scope.ServiceProvider.GetRequiredService<OrganizationService>().GetRoleAsync(organization, person)).Should().Be(OrganizationRoleConstants.Editor);
        }
    }

    private static AccountsHost CreateHost(SessionTests.RevocationLog revocations) => AccountsHost.Create(
        identity => identity
            .AddRoles()
            .AddSessions()
            .AddOrganizations(o => o.Roles[OrganizationRoleConstants.Editor] = ["content:edit"])
            .AddInvitations(),
        services: services => services.AddScoped<ISessionRevocationHandler>(_ => revocations));

    private static AccountsHost CreateSignUpHost(FakeTelegramBotClient bot, SessionTests.RevocationLog revocations) => AccountsHost.Create(
        identity => identity
            .AddSessions()
            .AddSignIn()
            .AddDatabaseOtpStore()
            .AddOrganizations(o => o.Roles[OrganizationRoleConstants.Editor] = ["content:edit"])
            .AddInvitations()
            .AddPhoneSignIn(
                telegram =>
                {
                    telegram.IsEnabled = true;
                    telegram.BotToken = "123456:TEST-token";
                    telegram.BotUserName = "Ocharo_Bot";
                    telegram.WebhookSecret = "hook-secret";
                },
                phones => phones.AllowedCountries = ["UZ"])
            .AddProfilePictures()
            .AddInviteSignUp()
            .AddAccountEndpoints(signUp =>
            {
                signUp.Profile.DisplayName = ProfileFieldMode.Optional;
                signUp.Profile.Picture = ProfileFieldMode.Off;
            }),
        services: services =>
        {
            services.AddOtpService(o => o.HashKey = HashKey);
            services.RemoveAll<ITelegramBotClient>();
            services.AddSingleton<ITelegramBotClient>(bot);
            services.AddScoped<ISessionRevocationHandler>(_ => revocations);
        });

    /// <summary>Reloads the person in a fresh scope, runs <paramref name="change"/> on them, and returns their stamp from before.</summary>
    private static async Task<string?> ChangeAsync(AccountsHost host, Guid userId, Func<IServiceProvider, IdentityUser, Task<IdentityResult>> change)
    {
        var before = (await host.ReloadAsync(userId)).SecurityStamp;
        await using var scope = host.Scope();
        var person = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(userId))!;
        (await change(scope.ServiceProvider, person)).IsSuccess.Should().BeTrue();
        return before;
    }

    private static async Task ShouldBeSignedOutAsync(AccountsHost host, SessionTests.RevocationLog revocations, Guid userId, string? stampBefore, params IdentitySession[] sessions)
    {
        (await host.ReloadAsync(userId)).SecurityStamp.Should().NotBe(stampBefore, "a role change rotates the stamp");
        foreach (var session in sessions)
        {
            var row = await RowAsync(host, session.Id);
            row.RevokedAt.Should().NotBeNull("the session ends at once, not when next seen");
            row.RevokeReason.Should().Be(SessionRevokeReasonConstants.RolesChanged);
            (await ValidateAsync(host, session.Id, userId)).Should().BeFalse();
        }

        revocations.Revoked.Should().Contain(sessions.Select(session => session.Id), "what the sessions issued ends with them");
    }

    private static async Task ShouldBeLiveAsync(AccountsHost host, IdentitySession session, string because = "")
    {
        (await RowAsync(host, session.Id)).RevokedAt.Should().BeNull(because);
        (await ValidateAsync(host, session.Id, session.UserId)).Should().BeTrue(because);
    }

    private static async Task<IdentitySession> StartAsync(AccountsHost host, Guid userId)
    {
        await using var scope = host.Scope();
        return await scope.ServiceProvider.GetRequiredService<UserSessionService>().StartAsync(await host.ReloadAsync(userId), [AuthenticationMethodConstants.Password]);
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

    private static async Task<IdentityOrganization> CreateOrganizationAsync(AccountsHost host, Guid ownerId)
    {
        await using var scope = host.Scope();
        var owner = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(ownerId))!;
        var organization = new IdentityOrganization { Name = "Ocharo", Slug = "ocharo" };
        (await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateAsync(organization, owner)).IsSuccess.Should().BeTrue();
        return organization;
    }

    private static async Task AddAsync(AccountsHost host, IdentityOrganization organization, Guid userId)
    {
        await using var scope = host.Scope();
        var person = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(userId))!;
        (await scope.ServiceProvider.GetRequiredService<OrganizationService>().AddMemberAsync(organization, person, OrganizationRoleConstants.Editor)).IsSuccess.Should().BeTrue();
    }

    private static async Task<IdentityInvitation> InviteAsync(AccountsHost host, IdentityOrganization organization, Guid ownerId, string? email)
    {
        await using var scope = host.Scope();
        var owner = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(ownerId))!;
        var created = await scope.ServiceProvider.GetRequiredService<InvitationService>().CreateAsync(organization, owner, OrganizationRoleConstants.Editor, null, email);
        created.IsSuccess.Should().BeTrue();
        return created.Invitation!;
    }

    private static async Task<string> TokenAsync(AccountsHost host, IdentityOrganization organization, Guid ownerId)
    {
        await using var scope = host.Scope();
        var owner = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(ownerId))!;
        return (await scope.ServiceProvider.GetRequiredService<InvitationService>().CreateAsync(organization, owner, OrganizationRoleConstants.Editor, null, null)).Token!;
    }

    private static async Task<InviteSignUpStartResult> StartSignUpAsync(AccountsHost host, string token)
    {
        await using var scope = host.Scope();
        var started = await scope.ServiceProvider.GetRequiredService<InviteSignUpService>().StartAsync(new InviteSignUpStartRequest { Token = token });
        started.Status.Should().Be(InviteSignUpStartStatus.Started);
        return started;
    }

    /// <summary>Polls the finished sign-up as the starting browser and starts the session its sign-in makes, as the poll route does.</summary>
    private static async Task<IdentitySession> SignInFromPollAsync(AccountsHost host, InviteSignUpStartResult started)
    {
        await using var scope = host.Scope();
        var poll = await scope.ServiceProvider.GetRequiredService<InviteSignUpService>().PollAsync(started.VerificationId!.Value, started.BrowserSecret);
        poll.State.Should().Be(InviteSignUpState.Completed);
        return await scope.ServiceProvider.GetRequiredService<UserSessionService>().StartAsync(poll.SignInAs!, [AuthenticationMethodConstants.OneTimePassword]);
    }

    /// <summary>Opens the start link in the bot and shares <paramref name="phone"/> as the sender's own contact.</summary>
    private static async Task ShareAsync(AccountsHost host, InviteSignUpStartResult started, string phone, long chatId)
    {
        var link = started.TelegramLinkUrl!;
        var token = link[(link.IndexOf('=') + 1)..];
        await using var scope = host.Scope();
        var updates = scope.ServiceProvider.GetRequiredService<TelegramUpdateHandler>();
        await updates.HandleAsync(Update(chatId, message => message.Text = "/start " + token));
        await updates.HandleAsync(Update(chatId, message => message.Contact = new Contact { PhoneNumber = phone, FirstName = "Lola", UserId = chatId }));
    }

    private static Update Update(long chatId, Action<Message> shape)
    {
        var message = new Message
        {
            Id = 1,
            Date = DateTime.UtcNow,
            Chat = new Chat { Id = chatId, Type = ChatType.Private },
            From = new User { Id = chatId, IsBot = false, FirstName = "Lola", LanguageCode = "en" },
        };
        shape(message);
        return new Update { Id = 1, Message = message };
    }
}
