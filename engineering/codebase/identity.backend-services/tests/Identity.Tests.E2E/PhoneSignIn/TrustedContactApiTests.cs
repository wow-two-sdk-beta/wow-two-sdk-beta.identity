using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SkiaSharp;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Admin;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Roles;
using WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;
using WoW.Two.Sdk.Beta.Be.Testing;

namespace Identity.Tests.E2E.PhoneSignIn;

/// <summary>
/// Trusted contacts through the real service with a fake bot: an owner invites with themselves as the contact; the invitee
/// signs up with the code the owner's Telegram receives, holding no phone; the owner's number picks the new account; the
/// new account adds its own phone through the bot; a later sign-in through the contact sends the account's own chat a
/// notice whose button revokes the contact and ends the sessions started through them. Beside the flow: the invitation's
/// contact checks, the sign-up's one transaction, a code sent to a contact from the account's own number, own-phone
/// sessions for revoking and changing, a contact leaving, and an instance role holder kept from contacts.
/// </summary>
public sealed class TrustedContactApiTests
{
    private const string Secret = "hook-secret_e2e";
    private const string BotUserName = "ocharo_e2e_bot";
    private const string OwnerPhone = "+998907777777";
    private const long OwnerChat = 9101;
    private const string MemberPhone = "+998905555555";
    private const long MemberChat = 9202;
    private const string MaftunaPhone = "+998901234567";
    private const long MaftunaChat = 9303;

    [Fact]
    public async Task Flow_ShouldSignUpThroughTheInviter_PickTheAccount_AddAPhone_AndRevokeFromTheNotice()
    {
        var connectionString = await PostgresTestServer.CreateDatabaseAsync();
        try
        {
            var bot = new FakeTelegramBotClient();
            await using var host = CreateHost(connectionString, bot);
            var (organizationId, ownerId) = await SeedOwnerAsync(host);
            using var owner = Client(host);
            await SignInOwnNumberAsync(host, bot, owner, OwnerPhone, OwnerChat);

            // Invite with the inviter as the trusted contact; the members list says whose phone may be one.
            var members = await DataAsync(await owner.GetAsync($"/api/organizations/{organizationId}/members"));
            var self = members.EnumerateArray().Single(member => member.GetProperty("userId").GetGuid() == ownerId);
            self.GetProperty("isPhoneConfirmed").GetBoolean().Should().BeTrue();
            self.GetProperty("trustedContacts").GetArrayLength().Should().Be(0);
            var created = await DataAsync(await owner.PostAsJsonAsync($"/api/organizations/{organizationId}/invitations", new { role = "owner", trustedContactUserId = ownerId }));
            var token = created.GetProperty("token").GetString()!;
            created.GetProperty("invitation").GetProperty("trustedContactUserId").GetGuid().Should().Be(ownerId);

            // Sign up through the contact: the code reaches the owner's Telegram, naming the person and the app.
            using var maftuna = Client(host);
            (await DataAsync(await maftuna.GetAsync($"/api/account/invitations/{token}"))).GetProperty("trustedContactHint").GetString().Should().Be("S***");
            var started = await DataAsync(await StartSignUpAsync(maftuna, token, "Maftuna Karimova", "trusted-contact"));
            started.GetProperty("isCodeSentToTrustedContact").GetBoolean().Should().BeTrue();
            started.GetProperty("telegramLinkUrl").ValueKind.Should().Be(JsonValueKind.Null);
            var signUpMessage = bot.To(OwnerChat).Last().Text;
            signUpMessage.Should().Contain("Maftuna Karimova").And.Contain("trusted contact");
            var signUpCode = bot.LastCode(OwnerChat);
            using (var wrong = await maftuna.PostAsJsonAsync("/api/account/invitations/sign-up/verify", new { code = Wrong(signUpCode) }))
                (await CodeOfAsync(wrong)).Should().Be("InvalidPhoneCode");
            (await maftuna.PostAsJsonAsync("/api/account/invitations/sign-up/verify", new { code = signUpCode })).StatusCode.Should().Be(HttpStatusCode.NoContent);
            var maftunaId = Guid.Parse((await DataAsync(await maftuna.GetAsync("/api/account/manage/info"))).GetProperty("userId").GetString()!);

            await using (var scope = host.Services.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                var person = await context.Users.SingleAsync(u => u.Id == maftunaId);
                person.PhoneNumber.Should().BeNull("a sign-up through the contact holds no phone");
                person.DisplayName.Should().Be("Maftuna Karimova");
                (await context.Memberships.SingleAsync(m => m.UserId == maftunaId)).Role.Should().Be("owner");
                (await context.TrustedContacts.SingleAsync(c => c.UserId == maftunaId)).Should().BeEquivalentTo(new { ContactUserId = ownerId, CreatedByUserId = ownerId }, o => o.ExcludingMissingMembers());
                (await context.Sessions.SingleAsync(s => s.UserId == maftunaId)).TrustedContactUserId.Should().Be(ownerId);
                var logged = await context.SignInLog.SingleAsync(e => e.UserId == maftunaId && e.Result == "succeeded");
                logged.Should().BeEquivalentTo(new { Method = "trusted_contact_code", TrustedContactUserId = (Guid?)ownerId }, o => o.ExcludingMissingMembers());
            }

            // A contact session neither revokes nor changes; it adds a first phone. The last contact of a phoneless account stays.
            var contacts = await DataAsync(await maftuna.GetAsync("/api/account/manage/trusted-contacts"));
            contacts.GetProperty("canRevoke").GetBoolean().Should().BeFalse();
            contacts.GetProperty("items").EnumerateArray().Single().GetProperty("isDeliverable").GetBoolean().Should().BeTrue();
            (await CodeOfAsync(await maftuna.DeleteAsync($"/api/account/manage/trusted-contacts/{ownerId}"))).Should().Be("MainPhoneRequired");
            var phone = await DataAsync(await maftuna.GetAsync("/api/account/manage/phone"));
            (phone.GetProperty("isConfirmed").GetBoolean(), phone.GetProperty("canChange").GetBoolean()).Should().Be((false, true));
            var trustedBy = (await DataAsync(await owner.GetAsync("/api/account/manage/trusted-by"))).EnumerateArray().Single();
            (trustedBy.GetProperty("userId").GetGuid(), trustedBy.GetProperty("canLeave").GetBoolean()).Should().Be((maftunaId, false));
            (await CodeOfAsync(await owner.DeleteAsync($"/api/account/manage/trusted-by/{maftunaId}"))).Should().Be("LastTrustedContact");

            // The contact's own number picks the account; a phoneless account gets no notice.
            using var picked = Client(host);
            host.Clock.Advance(TimeSpan.FromSeconds(31));
            var choice = await VerifyOwnNumberAsync(host, bot, picked, OwnerPhone, OwnerChat);
            var accounts = choice.GetProperty("accounts").EnumerateArray().ToList();
            accounts.Select(a => (a.GetProperty("userId").GetGuid(), a.GetProperty("isSelf").GetBoolean())).Should().Equal((ownerId, true), (maftunaId, false));
            (await picked.PostAsJsonAsync("/api/account/phone-sign-in/choose?useCookies=true", new { token = choice.GetProperty("token").GetString(), userId = maftunaId })).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await picked.GetAsync("/api/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.OK);
            using (var reused = await Client(host).PostAsJsonAsync("/api/account/phone-sign-in/choose?useCookies=true", new { token = choice.GetProperty("token").GetString(), userId = maftunaId }))
                (await CodeOfAsync(reused)).Should().Be("InvalidAccountChoice", "a choice token works once");
            bot.Sent.Should().NotContain(message => message.ReplyMarkup is InlineKeyboardMarkup, "an account with no own phone hears of nothing");
            var sessions = await DataAsync(await picked.GetAsync("/api/account/manage/sessions"));
            sessions.EnumerateArray().Select(s => s.GetProperty("trustedContactDisplayName").GetString()).Should().AllBe("Sultonbek");

            // Adding the first phone works from a contact session, through the bot's add-phone link.
            var link = (await DataAsync(await maftuna.PostAsync("/api/account/manage/phone/telegram-link", null))).GetProperty("telegramLinkUrl").GetString()!;
            await ShareAsync(maftuna, link, MaftunaChat, MaftunaPhone);
            bot.To(MaftunaChat).Last().Text.Should().Contain("now on your account");
            phone = await DataAsync(await maftuna.GetAsync("/api/account/manage/phone"));
            (phone.GetProperty("isConfirmed").GetBoolean(), phone.GetProperty("canChange").GetBoolean()).Should().Be((true, false), "changing it needs an own-phone session");
            (await CodeOfAsync(await maftuna.PostAsync("/api/account/manage/phone/telegram-link", null))).Should().Be("MainPhoneRequired");

            // A sign-in through the contact now sends the account's own chat a notice; its button revokes and ends those sessions.
            using var again = Client(host);
            host.Clock.Advance(TimeSpan.FromSeconds(31));
            var second = await VerifyOwnNumberAsync(host, bot, again, OwnerPhone, OwnerChat);
            (await again.PostAsJsonAsync("/api/account/phone-sign-in/choose?useCookies=true", new { token = second.GetProperty("token").GetString(), userId = maftunaId })).StatusCode.Should().Be(HttpStatusCode.NoContent);
            var notice = bot.To(MaftunaChat).Last();
            notice.Text.Should().Contain("Sultonbek").And.Contain("revoke their trust");
            var button = ((InlineKeyboardMarkup)notice.ReplyMarkup!).InlineKeyboard.Single().Single();
            button.Text.Should().Be("Revoke trust");

            (await PostUpdateAsync(again, CallbackUpdate(button.CallbackData!, MaftunaChat))).StatusCode.Should().Be(HttpStatusCode.OK);
            bot.TextEdits.Last().Text.Should().Be("Trust revoked");
            bot.Answers.Last().Text.Should().Be("Trust revoked");
            foreach (var throughContact in new[] { maftuna, picked, again })
                (await throughContact.GetAsync("/api/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "revoking the contact ends every session started through them");
            await using (var scope = host.Services.CreateAsyncScope())
                (await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().TrustedContacts.AnyAsync()).Should().BeFalse();

            // Her own number now signs her straight in, with an own-phone session.
            using var own = Client(host);
            await SignInOwnNumberAsync(host, bot, own, MaftunaPhone, MaftunaChat);
            (await DataAsync(await own.GetAsync("/api/account/manage/trusted-contacts"))).GetProperty("canRevoke").GetBoolean().Should().BeTrue();
            (await DataAsync(await own.GetAsync("/api/account/manage/phone"))).GetProperty("canChange").GetBoolean().Should().BeTrue();
        }
        finally
        {
            await PostgresTestServer.DropDatabaseAsync(connectionString);
        }
    }

    [Fact]
    public async Task Invitation_ShouldTakeOnlyAQualifiedContact_AndASignUpThroughOneShouldBeOneTransaction()
    {
        var connectionString = await PostgresTestServer.CreateDatabaseAsync();
        try
        {
            var bot = new FakeTelegramBotClient();
            await using var host = CreateHost(connectionString, bot);
            var (organizationId, ownerId) = await SeedOwnerAsync(host);
            var outsider = await CreatePersonAsync(host, "Outsider", "+998903333333", chatId: 9404);
            var unlinked = await CreatePersonAsync(host, "Unlinked", "+998904444444", chatId: null);
            await JoinAsync(host, organizationId, unlinked, "editor");
            var member = await CreatePersonAsync(host, "Member", MemberPhone, MemberChat);
            await JoinAsync(host, organizationId, member, "editor");
            using var owner = Client(host);
            await SignInOwnNumberAsync(host, bot, owner, OwnerPhone, OwnerChat);

            (await CodeOfAsync(await owner.PostAsJsonAsync($"/api/organizations/{organizationId}/invitations", new { role = "editor", trustedContactUserId = outsider }))).Should().Be("InvalidTrustedContact");
            (await CodeOfAsync(await owner.PostAsJsonAsync($"/api/organizations/{organizationId}/invitations", new { role = "editor", trustedContactUserId = unlinked }))).Should().Be("TrustedContactUnavailable");
            var token = (await DataAsync(await owner.PostAsJsonAsync($"/api/organizations/{organizationId}/invitations", new { role = "editor", trustedContactUserId = member }))).GetProperty("token").GetString()!;
            var plain = (await DataAsync(await owner.PostAsJsonAsync($"/api/organizations/{organizationId}/invitations", new { role = "editor" }))).GetProperty("token").GetString()!;
            (await CodeOfAsync(await StartSignUpAsync(Client(host), plain, "Nobody", "trusted-contact"))).Should().Be("NoTrustedContact");

            // The code goes to another member's Telegram; withdrawing the invitation before it is typed undoes the whole sign-up.
            using var invitee = Client(host);
            (await StartSignUpAsync(invitee, token, "Lola", "trusted-contact")).StatusCode.Should().Be(HttpStatusCode.OK);
            var code = bot.LastCode(MemberChat);
            var invitationId = (await DataAsync(await owner.GetAsync($"/api/organizations/{organizationId}/invitations")))
                .EnumerateArray().Single(i => i.GetProperty("trustedContactUserId").ValueKind == JsonValueKind.String).GetProperty("id").GetGuid();
            (await owner.DeleteAsync($"/api/organizations/{organizationId}/invitations/{invitationId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await CodeOfAsync(await invitee.PostAsJsonAsync("/api/account/invitations/sign-up/verify", new { code }))).Should().Be("InvitationRevoked");

            await using var scope = host.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            (await context.Users.AnyAsync(u => u.DisplayName == "Lola")).Should().BeFalse("the refused join rolls the new person back");
            (await context.TrustedContacts.AnyAsync()).Should().BeFalse("and the contact with them");
            _ = ownerId;
        }
        finally
        {
            await PostgresTestServer.DropDatabaseAsync(connectionString);
        }
    }

    [Fact]
    public async Task OwnNumber_ShouldSendToItsChatByDefault_OrToAPickedContact_WhoseSignInTheOwnPhoneSessionRevokes()
    {
        var connectionString = await PostgresTestServer.CreateDatabaseAsync();
        try
        {
            var bot = new FakeTelegramBotClient();
            await using var host = CreateHost(connectionString, bot);
            var (organizationId, ownerId) = await SeedOwnerAsync(host);
            var maftunaId = await CreatePersonAsync(host, "Maftuna", MaftunaPhone, MaftunaChat);
            await JoinAsync(host, organizationId, maftunaId, "editor");
            var memberId = await CreatePersonAsync(host, "Member", MemberPhone, MemberChat);
            await JoinAsync(host, organizationId, memberId, "editor");
            await RecordAsync(host, maftunaId, ownerId);
            await RecordAsync(host, maftunaId, memberId);

            // Without a target the code goes to the number's own chat, as before.
            using var device = Client(host);
            (await DataAsync(await device.PostAsJsonAsync("/api/account/phone-sign-in/send", new { phone = MaftunaPhone }))).GetProperty("isCodeSent").GetBoolean().Should().BeTrue();
            bot.To(MaftunaChat).Should().ContainSingle();

            // Targets are masked and opaque; picking one sends the code to that contact, naming the account.
            var targets = (await DataAsync(await device.PostAsJsonAsync("/api/account/phone-sign-in/targets", new { phone = MaftunaPhone }))).GetProperty("trustedContacts").EnumerateArray().ToList();
            targets.Select(t => t.GetProperty("label").GetString()).Should().Equal("S*** · +998*******77", "M*** · +998*******55");
            (await DataAsync(await Client(host).PostAsJsonAsync("/api/account/phone-sign-in/targets", new { phone = "+998900000000" }))).GetProperty("trustedContacts").GetArrayLength().Should().Be(0);
            (await CodeOfAsync(await device.PostAsJsonAsync("/api/account/phone-sign-in/send", new { phone = MaftunaPhone, targetId = targets[1].GetProperty("id").GetString() })))
                .Should().Be("PhoneCodeRateLimited", "the number's own 30 s window covers a contact send too");
            host.Clock.Advance(TimeSpan.FromSeconds(31));
            (await CodeOfAsync(await device.PostAsJsonAsync("/api/account/phone-sign-in/send", new { phone = MaftunaPhone, targetId = "not-a-target" }))).Should().Be("TrustedContactNotFound");
            (await DataAsync(await device.PostAsJsonAsync("/api/account/phone-sign-in/send", new { phone = MaftunaPhone, targetId = targets[1].GetProperty("id").GetString() }))).GetProperty("isCodeSent").GetBoolean().Should().BeTrue();
            bot.To(MemberChat).Last().Text.Should().Contain("Maftuna").And.Contain("Share it only if you are signing in for them");
            var contactCode = bot.LastCode(MemberChat);
            (await device.PostAsJsonAsync("/api/account/phone-sign-in?useCookies=true", new { phone = MaftunaPhone, code = contactCode })).StatusCode.Should().Be(HttpStatusCode.NoContent);
            bot.To(MaftunaChat).Last().Text.Should().Contain("Member", "the account has its own phone, so it hears of the contact's sign-in");

            // The contact's own number signs in to their own account with that code no more: a bound code serves one account.
            using (var misuse = await Client(host).PostAsJsonAsync("/api/account/phone-sign-in?useCookies=true", new { phone = MemberPhone, code = contactCode }))
                (await CodeOfAsync(misuse)).Should().Be("InvalidPhoneCode");

            // An own-phone session revokes the contact, ending the session through them; the other contact may then leave.
            using var own = Client(host);
            host.Clock.Advance(TimeSpan.FromSeconds(31));
            await SignInOwnNumberAsync(host, bot, own, MaftunaPhone, MaftunaChat);
            var listed = await DataAsync(await own.GetAsync("/api/account/manage/trusted-contacts"));
            listed.GetProperty("canRevoke").GetBoolean().Should().BeTrue();
            (await own.DeleteAsync($"/api/account/manage/trusted-contacts/{memberId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await device.GetAsync("/api/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await own.GetAsync("/api/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.OK, "the own-phone session stays");
            (await CodeOfAsync(await own.DeleteAsync($"/api/account/manage/trusted-contacts/{memberId}"))).Should().Be("TrustedContactNotFound");

            using var ownerBrowser = Client(host);
            await SignInAsChosenAsync(host, bot, ownerBrowser, OwnerPhone, OwnerChat, ownerId);
            var trustedBy = (await DataAsync(await ownerBrowser.GetAsync("/api/account/manage/trusted-by"))).EnumerateArray().Single();
            trustedBy.GetProperty("canLeave").GetBoolean().Should().BeTrue("the account holds its own phone");
            (await ownerBrowser.DeleteAsync($"/api/account/manage/trusted-by/{maftunaId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await DataAsync(await own.GetAsync("/api/account/manage/trusted-contacts"))).GetProperty("items").GetArrayLength().Should().Be(0);
        }
        finally
        {
            await PostgresTestServer.DropDatabaseAsync(connectionString);
        }
    }

    [Fact]
    public async Task InstanceRoleHolder_ShouldHaveNoContact_NorBeOfferedThroughOne()
    {
        var connectionString = await PostgresTestServer.CreateDatabaseAsync();
        try
        {
            var bot = new FakeTelegramBotClient();
            await using var host = CreateHost(connectionString, bot);
            var (_, ownerId) = await SeedOwnerAsync(host);
            var maftunaId = await CreatePersonAsync(host, "Maftuna", MaftunaPhone, MaftunaChat);
            await RecordAsync(host, maftunaId, ownerId);
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var person = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(maftunaId))!;
                (await scope.ServiceProvider.GetRequiredService<InstanceRoleService>().GrantAsync(person, InstanceRoleConstants.Support)).Result.IsSuccess.Should().BeTrue();
                var support = await CreatePersonAsync(host, "Support", MemberPhone, MemberChat);
                (await scope.ServiceProvider.GetRequiredService<TrustedContactService>().RecordAsync(ownerId, support, support)).Errors.Single().Code
                    .Should().Be(TrustedContactErrorCodeConstants.InstanceRoleHolder, "the instance admin owner cannot gain a contact");
            }

            using var device = Client(host);
            (await DataAsync(await device.PostAsJsonAsync("/api/account/phone-sign-in/targets", new { phone = MaftunaPhone }))).GetProperty("trustedContacts").GetArrayLength().Should().Be(0);
            using var ownerBrowser = Client(host);
            await SignInOwnNumberAsync(host, bot, ownerBrowser, OwnerPhone, OwnerChat);
            (await ownerBrowser.GetAsync("/api/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.OK, "trusted only by a role holder, the owner signs straight in");
        }
        finally
        {
            await PostgresTestServer.DropDatabaseAsync(connectionString);
        }
    }

    private static WebApiTestHost<Program> CreateHost(string connectionString, FakeTelegramBotClient bot)
    {
        var host = new WebApiTestHost<Program>
        {
            ConfigureConfigurationHook = configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseSettings:ConnectionString"] = connectionString,
                ["AllowedHosts"] = "localhost",
                ["Logging:LogLevel:Default"] = "Warning",
                ["Identity:Server:Issuer"] = "https://localhost/",
                ["Identity:SignUp:Mode"] = "Invite",
                ["Identity:SignUp:Profile:DisplayName"] = "Required",
                ["Identity:SignUp:Profile:Picture"] = "Optional",
                ["Identity:Methods:Password:IsEnabled"] = "false",
                ["Identity:Methods:Email:IsEnabled"] = "false",
                ["Identity:Telegram:IsEnabled"] = "true",
                ["Identity:Telegram:BotToken"] = "123456:E2E-token",
                ["Identity:Telegram:BotUserName"] = BotUserName,
                ["Identity:Telegram:WebhookSecret"] = Secret,
                ["Identity:Phone:AllowedCountries:0"] = "UZ",
                ["Identity:Otp:HashKey"] = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=",
                ["Identity:Otp:OriginRateLimitWindow"] = "00:00:00",
                ["Identity:Otp:Messages:AppName"] = "Ocharo",
                ["Identity:TrustedContacts:IsEnabled"] = "true",
            }),
            ConfigureServicesHook = services =>
            {
                services.RemoveAll<ITelegramBotClient>();
                services.AddSingleton<ITelegramBotClient>(bot);
            },
        };
        host.Clock.SetUtcNow(DateTimeOffset.UtcNow);
        return host;
    }

    /// <summary>The instance admin as <c>admin create</c> makes them, owner of Ocharo, their number linked to the bot.</summary>
    private static async Task<(Guid OrganizationId, Guid OwnerId)> SeedOwnerAsync(WebApiTestHost<Program> host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var created = await ActivatorUtilities.CreateInstance<InstanceAdminService>(scope.ServiceProvider)
            .CreateAsync(new InstanceAdminRequest { DisplayName = "Sultonbek", PhoneNumber = OwnerPhone });
        created.Status.Should().Be(InstanceAdminStatus.Created, created.Error);
        var owner = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(created.UserId!.Value))!;
        var organization = new IdentityOrganization { Name = "Ocharo", Slug = "ocharo" };
        (await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateAsync(organization, owner)).IsSuccess.Should().BeTrue();
        await LinkAsync(scope.ServiceProvider, OwnerPhone, OwnerChat);
        return (organization.Id, owner.Id);
    }

    /// <summary>A person holding <paramref name="phone"/> confirmed, linked to <paramref name="chatId"/> unless it is null.</summary>
    private static async Task<Guid> CreatePersonAsync(WebApiTestHost<Program> host, string name, string phone, long? chatId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var person = new IdentityUser { UserName = phone, DisplayName = name, PhoneNumber = phone, IsPhoneNumberConfirmed = true };
        (await scope.ServiceProvider.GetRequiredService<UserAccountService>().CreateAsync(person)).IsSuccess.Should().BeTrue();
        if (chatId is { } chat)
            await LinkAsync(scope.ServiceProvider, phone, chat);
        return person.Id;
    }

    private static async Task LinkAsync(IServiceProvider services, string phone, long chatId)
        => await services.GetRequiredService<ITelegramLinkRepository>().SaveLinkAsync(new IdentityTelegramLink
        {
            Id = Guid.CreateVersion7(),
            BotUserName = BotUserName,
            PhoneNumber = phone,
            ChatId = chatId,
            TelegramUserId = chatId,
            LinkedAt = DateTimeOffset.UtcNow,
        });

    private static async Task JoinAsync(WebApiTestHost<Program> host, Guid organizationId, Guid userId, string role)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var organizations = scope.ServiceProvider.GetRequiredService<OrganizationService>();
        var person = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(userId))!;
        (await organizations.AddMemberAsync((await organizations.FindByIdAsync(organizationId))!, person, role)).IsSuccess.Should().BeTrue();
    }

    private static async Task RecordAsync(WebApiTestHost<Program> host, Guid userId, Guid contactUserId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<TrustedContactService>().RecordAsync(userId, contactUserId, contactUserId)).IsSuccess.Should().BeTrue();
    }

    /// <summary>Signs <paramref name="client"/> in with a number's own code, straight in: nobody trusts its person.</summary>
    private static async Task SignInOwnNumberAsync(WebApiTestHost<Program> host, FakeTelegramBotClient bot, HttpClient client, string phone, long chatId)
    {
        (await DataAsync(await client.PostAsJsonAsync("/api/account/phone-sign-in/send", new { phone }))).GetProperty("isCodeSent").GetBoolean().Should().BeTrue();
        using var signedIn = await client.PostAsJsonAsync("/api/account/phone-sign-in?useCookies=true", new { phone, code = bot.LastCode(chatId) });
        signedIn.StatusCode.Should().Be(HttpStatusCode.NoContent, await signedIn.Content.ReadAsStringAsync());
        host.Clock.Advance(TimeSpan.FromSeconds(31));
    }

    /// <summary>Verifies a trusted contact's own number and returns the account choice it answers with.</summary>
    private static async Task<JsonElement> VerifyOwnNumberAsync(WebApiTestHost<Program> host, FakeTelegramBotClient bot, HttpClient client, string phone, long chatId)
    {
        (await DataAsync(await client.PostAsJsonAsync("/api/account/phone-sign-in/send", new { phone }))).GetProperty("isCodeSent").GetBoolean().Should().BeTrue();
        var answer = await DataAsync(await client.PostAsJsonAsync("/api/account/phone-sign-in?useCookies=true", new { phone, code = bot.LastCode(chatId) }));
        host.Clock.Advance(TimeSpan.FromSeconds(31));
        return answer.GetProperty("accountChoice");
    }

    private static async Task SignInAsChosenAsync(WebApiTestHost<Program> host, FakeTelegramBotClient bot, HttpClient client, string phone, long chatId, Guid userId)
    {
        var choice = await VerifyOwnNumberAsync(host, bot, client, phone, chatId);
        (await client.PostAsJsonAsync("/api/account/phone-sign-in/choose?useCookies=true", new { token = choice.GetProperty("token").GetString(), userId })).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private static HttpClient Client(WebApiTestHost<Program> host)
        => host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });

    private static async Task<HttpResponseMessage> StartSignUpAsync(HttpClient client, string token, string name, string verification)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(name), "displayName");
        form.Add(new StringContent(verification), "verification");
        var file = new ByteArrayContent(Png());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "picture", "me.png");
        return await client.PostAsync($"/api/account/invitations/{token}/sign-up?clientId=desk", form);
    }

    /// <summary>Opens a start link in the bot from <paramref name="chatId"/> and shares <paramref name="phone"/> as the sender's own contact.</summary>
    private static async Task ShareAsync(HttpClient client, string link, long chatId, string phone)
    {
        (await PostUpdateAsync(client, MessageUpdate(chatId, $"\"text\": \"/start {link[(link.IndexOf('=') + 1)..]}\""))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostUpdateAsync(client, MessageUpdate(chatId, $"\"contact\": {{ \"phone_number\": \"{phone.TrimStart('+')}\", \"first_name\": \"Maftuna\", \"user_id\": {chatId} }}"))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<HttpResponseMessage> PostUpdateAsync(HttpClient client, string update)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/telegram/webhook") { Content = new StringContent(update, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", Secret);
        return await client.SendAsync(request);
    }

    private static string MessageUpdate(long chatId, string body) => $$"""
        {
          "update_id": 1,
          "message": {
            "message_id": 1,
            "date": 1793520000,
            "chat": { "id": {{chatId}}, "type": "private", "first_name": "Maftuna" },
            "from": { "id": {{chatId}}, "is_bot": false, "first_name": "Maftuna", "language_code": "en" },
            {{body}}
          }
        }
        """;

    private static string CallbackUpdate(string data, long fromId) => $$"""
        {
          "update_id": 2,
          "callback_query": {
            "id": "press-1",
            "from": { "id": {{fromId}}, "is_bot": false, "first_name": "Maftuna", "language_code": "en" },
            "chat_instance": "1",
            "data": "{{data}}"
          }
        }
        """;

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.OK, body);
            using var document = JsonDocument.Parse(body);
            return document.RootElement.GetProperty("data").Clone();
        }
    }

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            using var problem = JsonDocument.Parse(body);
            return problem.RootElement.TryGetProperty("identityCode", out var code) ? code.GetString() : body;
        }
    }

    private static string Wrong(string code) => code == "000000" ? "111111" : "000000";

    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(300, 300);
        bitmap.Erase(SKColors.Coral);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
