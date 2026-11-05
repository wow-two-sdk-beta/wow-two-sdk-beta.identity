using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.AccessChanges;
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
using WoW.Two.Sdk.Beta.Be.Identity.Core.Signals;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;
using WoW.Two.Sdk.Beta.Be.Identity.Tests.PhoneSignIn;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Accounts;

/// <summary>
/// A role change signs nobody out: an instance role granted or removed, an organization joined (a member added, an invitation
/// redeemed, the invitation sign-up), a role changed, a member removed or the organization deleted leaves the person's stamp
/// and sessions alone. Every <see cref="IAccessChangeHandler"/> hears of what an app's tokens carry, a call that changes nothing
/// tells nobody, and the cookie reads its roles fresh on its next request.
/// </summary>
public sealed class RoleChangeSessionTests
{
    private const string Phone = "+998901234567";
    private const string NewPhone = "+998907654321";
    private const long KnownChat = 7272;
    private const long NewChat = 7373;
    private const string HashKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    [Fact]
    public async Task InstanceRole_GrantAndRemoval_ShouldSignNobodyOut_AndRaiseNoSignal()
    {
        var log = new Log();
        await using var host = CreateHost(log);
        var lola = await host.CreateUserAsync("lola");
        var laptop = await StartAsync(host, lola.Id);

        var stamp = await ChangeAsync(host, lola.Id, async (services, person) =>
            (await services.GetRequiredService<InstanceRoleService>().GrantAsync(person, InstanceRoleConstants.Admin)).Result);
        await ShouldStayAsync(host, log, lola.Id, stamp, laptop);

        stamp = await ChangeAsync(host, lola.Id, (services, person) =>
            services.GetRequiredService<UserRoleService>().RemoveFromRoleAsync(person, InstanceRoleConstants.Admin));
        await ShouldStayAsync(host, log, lola.Id, stamp, laptop);
        log.Changes.Should().BeEmpty("instance roles never enter an app's token");
    }

    [Fact]
    public async Task Joining_ByBeingAddedOrRedeemingAnInvitation_ShouldTellTheHandlers_AndSignNobodyOut()
    {
        var log = new Log();
        await using var host = CreateHost(log);
        var owner = await host.CreateUserAsync("owner");
        var organization = await CreateOrganizationAsync(host, owner.Id);
        log.Changes.Should().BeEmpty("the creator's next sign-in shows the membership");

        var ann = await host.CreateUserAsync("ann");
        var annSession = await StartAsync(host, ann.Id);
        var stamp = await ChangeAsync(host, ann.Id, (services, person) =>
            services.GetRequiredService<OrganizationService>().AddMemberAsync(organization, person, OrganizationRoleConstants.Editor));
        await ShouldStayAsync(host, log, ann.Id, stamp, annSession);
        log.ShouldHaveToldOnce(ann.Id, organization.Id, AccessChangeKind.Changed);

        var ben = await host.CreateUserAsync("ben", "ben@example.test");
        var benSession = await StartAsync(host, ben.Id);
        var invitation = await InviteAsync(host, organization, owner.Id, email: "ben@example.test");
        stamp = await ChangeAsync(host, ben.Id, (services, person) => services.GetRequiredService<InvitationService>().RedeemAsync(invitation, person));
        await ShouldStayAsync(host, log, ben.Id, stamp, benSession);
        log.ShouldHaveToldOnce(ben.Id, organization.Id, AccessChangeKind.Changed);

        var second = await InviteAsync(host, organization, owner.Id, email: null);
        await ChangeAsync(host, ben.Id, (services, person) => services.GetRequiredService<InvitationService>().RedeemAsync(second, person));
        log.ShouldHaveToldOnce(ben.Id, organization.Id, AccessChangeKind.Changed);
    }

    [Fact]
    public async Task RoleChange_Removal_AndDeletion_ShouldTellTheHandlers_AndLeaveEverySessionLive()
    {
        var log = new Log();
        await using var host = CreateHost(log);
        var owner = await host.CreateUserAsync("owner");
        var ann = await host.CreateUserAsync("ann");
        var organization = await CreateOrganizationAsync(host, owner.Id);
        await AddAsync(host, organization, ann.Id);
        var ownerSession = await StartAsync(host, owner.Id);
        var session = await StartAsync(host, ann.Id);
        log.Changes.Clear();

        await ChangeAsync(host, ann.Id, (services, person) =>
            services.GetRequiredService<OrganizationService>().ChangeRoleAsync(organization, person, OrganizationRoleConstants.Editor));
        log.Changes.Should().BeEmpty("the same role changes nothing");

        var stamp = await ChangeAsync(host, ann.Id, (services, person) =>
            services.GetRequiredService<OrganizationService>().ChangeRoleAsync(organization, person, OrganizationRoleConstants.Viewer));
        await ShouldStayAsync(host, log, ann.Id, stamp, session);
        log.ShouldHaveToldOnce(ann.Id, organization.Id, AccessChangeKind.Changed);
        log.Changes.Clear();

        stamp = await ChangeAsync(host, ann.Id, (services, person) =>
            services.GetRequiredService<OrganizationService>().RemoveMemberAsync(organization, person));
        await ShouldStayAsync(host, log, ann.Id, stamp, session);
        log.ShouldHaveToldOnce(ann.Id, organization.Id, AccessChangeKind.Removed);
        log.Changes.Clear();

        await AddAsync(host, organization, ann.Id);
        log.Changes.Clear();
        var annStamp = (await host.ReloadAsync(ann.Id)).SecurityStamp;
        var ownerStamp = await ChangeAsync(host, owner.Id, async (services, _) =>
            await services.GetRequiredService<OrganizationService>().DeleteAsync(organization));
        await ShouldStayAsync(host, log, ann.Id, annStamp, session);
        await ShouldStayAsync(host, log, owner.Id, ownerStamp, ownerSession);
        log.ShouldHaveToldOnce(ann.Id, organization.Id, AccessChangeKind.Removed);
        log.ShouldHaveToldOnce(owner.Id, organization.Id, AccessChangeKind.Removed);
    }

    [Theory]
    [InlineData(UserClaimsPrincipalFactory.DefaultAuthenticationType)]
    [InlineData(CookieAuthenticationDefaults.AuthenticationScheme)]
    public async Task Cookie_ShouldReadItsRolesFresh_OnTheNextRequest_KeepingWhatTheSignInDecided(string authenticationType)
    {
        var log = new Log();
        await using var host = CreateHost(log);
        var owner = await host.CreateUserAsync("owner");
        var ann = await host.CreateUserAsync("ann");
        var organization = await CreateOrganizationAsync(host, owner.Id);
        await AddAsync(host, organization, ann.Id);
        var role = $"{organization.Id:D}:{OrganizationRoleConstants.Viewer}";

        ClaimsPrincipal cookie;
        await using (var scope = host.Scope())
        {
            cookie = await scope.ServiceProvider.GetRequiredService<UserClaimsPrincipalFactory>()
                .CreateAsync(await host.ReloadAsync(ann.Id), authenticationType);
        }

        var identity = (ClaimsIdentity)cookie.Identity!;
        identity.AddClaim(new Claim("sid", Guid.NewGuid().ToString("D")));
        identity.AddClaim(new Claim(AuthenticationMethodConstants.ClaimType, AuthenticationMethodConstants.Password));
        cookie.FindAll(OrganizationClaimTypeConstants.Role).Select(claim => claim.Value).Should().NotContain(role);

        await ChangeAsync(host, ann.Id, (services, person) =>
            services.GetRequiredService<OrganizationService>().ChangeRoleAsync(organization, person, OrganizationRoleConstants.Viewer));

        await using (var scope = host.Scope())
        {
            var fresh = await scope.ServiceProvider.GetRequiredService<IClaimsTransformation>().TransformAsync(cookie);
            fresh.FindAll(OrganizationClaimTypeConstants.Role).Select(claim => claim.Value).Should().Equal(role);
            fresh.FindFirst("sid")!.Value.Should().Be(cookie.FindFirst("sid")!.Value);
            fresh.FindFirst(AuthenticationMethodConstants.ClaimType)!.Value.Should().Be(AuthenticationMethodConstants.Password);
        }

        await ChangeAsync(host, ann.Id, (services, person) =>
            services.GetRequiredService<OrganizationService>().RemoveMemberAsync(organization, person));
        await using (var scope = host.Scope())
            (await scope.ServiceProvider.GetRequiredService<IClaimsTransformation>().TransformAsync(cookie)).FindAll(OrganizationClaimTypeConstants.Role).Should().BeEmpty();
    }

    [Fact]
    public async Task InviteSignUp_ShouldLeaveAKnownPersonsSessions_AndStillSignTheBrowserIn()
    {
        var bot = new FakeTelegramBotClient();
        var log = new Log();
        await using var host = CreateSignUpHost(bot, log);
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

        await ShouldStayAsync(host, log, known.Id, stamp, oldSession);
        var knownBrowser = await SignInFromPollAsync(host, knownSignUp);
        knownBrowser.UserId.Should().Be(known.Id);
        await ShouldBeLiveAsync(host, knownBrowser, "the poll signs in after the join");
        log.ShouldHaveToldOnce(known.Id, organization.Id, AccessChangeKind.Changed);

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

        public void ShouldHaveToldOnce(Guid person, Guid organization, AccessChangeKind kind)
            => Changes.Should().ContainSingle(change => change.Subject == person.ToString("D") && change.OrganizationId == organization.ToString("D") && change.Kind == kind);
    }

    private static AccountsHost CreateHost(Log log) => AccountsHost.Create(
        identity => identity
            .AddRoles()
            .AddSessions()
            .AddOrganizations(o => o.Roles[OrganizationRoleConstants.Editor] = ["content:edit"])
            .AddInvitations(),
        services: services =>
        {
            services.AddScoped<ISessionRevocationHandler>(_ => log);
            services.AddScoped<IAccessChangeHandler>(_ => log);
        });

    private static AccountsHost CreateSignUpHost(FakeTelegramBotClient bot, Log log) => AccountsHost.Create(
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
            services.AddScoped<ISessionRevocationHandler>(_ => log);
            services.AddScoped<IAccessChangeHandler>(_ => log);
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

    private static async Task ShouldStayAsync(AccountsHost host, Log log, Guid userId, string? stampBefore, params IdentitySession[] sessions)
    {
        (await host.ReloadAsync(userId)).SecurityStamp.Should().Be(stampBefore, "a role change rotates no stamp");
        foreach (var session in sessions)
            await ShouldBeLiveAsync(host, session, "a role change ends no session");

        log.Revoked.Should().BeEmpty();
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
