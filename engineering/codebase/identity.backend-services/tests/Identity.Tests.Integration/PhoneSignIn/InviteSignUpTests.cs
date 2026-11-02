using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SkiaSharp;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Invitations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.InviteSignUp;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.ProfilePictures;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Core.OneTimeCodes;
using WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Phones;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;
using WoW.Two.Sdk.Beta.Be.Identity.Tests.Accounts;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.PhoneSignIn;

/// <summary>
/// <see cref="InviteSignUpService"/> over PostgreSQL with a fake bot: a known person joins keeping their name and gaining
/// the picture, a bound invitation refuses another number, a number outside the allowed countries fails the sign-up, and
/// a poll with the wrong secret finds nothing.
/// </summary>
public sealed class InviteSignUpTests
{
    private const string Phone = "+998901234567";
    private const long ChatId = 7171;
    private const string HashKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    [Fact]
    public async Task KnownPerson_ShouldJoinWithTheRole_KeepTheirName_AndGainThePicture()
    {
        var bot = new FakeTelegramBotClient();
        await using var host = CreateHost(bot);
        var (organization, token) = await InviteAsync(host, phone: null);
        var known = await host.CreateUserAsync("lola");
        await using (var scope = host.Scope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<UserAccountService>();
            var tracked = (await accounts.FindByIdAsync(known.Id))!;
            tracked.PhoneNumber = Phone;
            tracked.IsPhoneNumberConfirmed = true;
            tracked.DisplayName = "Lola";
            await accounts.UpdateAsync(tracked);
        }

        var started = await StartAsync(host, token, "Someone Else");
        await ShareAsync(host, started, Phone);

        bot.To(ChatId).Last().Text.Should().Contain("Your number is verified");
        await using var check = host.Scope();
        var poll = await check.ServiceProvider.GetRequiredService<InviteSignUpService>().PollAsync(started.VerificationId!.Value, started.BrowserSecret);
        poll.State.Should().Be(InviteSignUpState.Completed);
        poll.SignInAs!.Id.Should().Be(known.Id);
        poll.SignInAs.DisplayName.Should().Be("Lola", "a known person keeps their name");
        (await check.ServiceProvider.GetRequiredService<IProfilePictureStore>().FindVersionAsync(known.Id)).Should().NotBeNull("a person without a picture gains it");
        (await check.ServiceProvider.GetRequiredService<OrganizationService>().GetRoleAsync(organization, poll.SignInAs)).Should().Be("editor");
        (await check.ServiceProvider.GetRequiredService<InviteSignUpService>().PollAsync(started.VerificationId.Value, started.BrowserSecret)).SignInAs
            .Should().BeNull("the browser is signed in once");
    }

    [Fact]
    public async Task BoundInvitation_ShouldRefuseAnotherNumber_AndAForeignNumberShouldFailTheSignUp()
    {
        var bot = new FakeTelegramBotClient();
        await using var host = CreateHost(bot);
        var (_, bound) = await InviteAsync(host, phone: Phone);

        var boundStart = await StartAsync(host, bound, "Maftuna");
        await ShareAsync(host, boundStart, "+998907654321");
        bot.To(ChatId).Last().Text.Should().Be("This invitation is for another phone number.");

        var (_, open) = await InviteAsync(host, phone: null, slug: "other");
        host.Time.Advance(TimeSpan.FromMinutes(1));
        var openStart = await StartAsync(host, open, "Maftuna");
        await ShareAsync(host, openStart, "+77011234567");
        bot.To(ChatId).Last().Text.Should().Be("Phone numbers from this country cannot sign up here.");

        await using var check = host.Scope();
        var signUp = check.ServiceProvider.GetRequiredService<InviteSignUpService>();
        (await signUp.PollAsync(openStart.VerificationId!.Value, openStart.BrowserSecret)).Should()
            .BeEquivalentTo(new { State = InviteSignUpState.Failed, FailureCode = IdentityErrorCodeConstants.PhoneCountryNotAllowed }, o => o.ExcludingMissingMembers());
        (await signUp.PollAsync(openStart.VerificationId.Value, "not-the-secret")).State.Should().Be(InviteSignUpState.NotFound);
        (await signUp.PollAsync(boundStart.VerificationId!.Value, boundStart.BrowserSecret)).State.Should().Be(InviteSignUpState.Pending, "a refused contact leaves the sign-up waiting");
    }

    private static AccountsHost CreateHost(FakeTelegramBotClient bot) => AccountsHost.Create(
        identity => identity
            .AddSignIn()
            .AddDatabaseOtpStore()
            .AddOrganizations(o => o.Roles["editor"] = ["content:edit"])
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
                signUp.Profile.DisplayName = ProfileFieldMode.Required;
                signUp.Profile.Picture = ProfileFieldMode.Optional;
            }),
        services: services =>
        {
            services.AddOtpService(o => o.HashKey = HashKey);
            services.RemoveAll<ITelegramBotClient>();
            services.AddSingleton<ITelegramBotClient>(bot);
        });

    private static async Task<(IdentityOrganization Organization, string Token)> InviteAsync(AccountsHost host, string? phone, string slug = "ocharo")
    {
        var owner = await host.CreateUserAsync("owner-" + slug);
        await using var scope = host.Scope();
        var organization = new IdentityOrganization { Name = slug, Slug = slug };
        (await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateAsync(organization, owner)).IsSuccess.Should().BeTrue();
        var created = await scope.ServiceProvider.GetRequiredService<InvitationService>().CreateAsync(organization, owner, "editor", phone, null);
        return (organization, created.Token!);
    }

    private static async Task<InviteSignUpStartResult> StartAsync(AccountsHost host, string token, string name)
    {
        await using var scope = host.Scope();
        var started = await scope.ServiceProvider.GetRequiredService<InviteSignUpService>().StartAsync(
            new InviteSignUpStartRequest { Token = token, DisplayName = name, Picture = Png() });
        started.Status.Should().Be(InviteSignUpStartStatus.Started);
        return started;
    }

    /// <summary>Opens the start link in the bot and shares <paramref name="phone"/> as the sender's own contact.</summary>
    private static async Task ShareAsync(AccountsHost host, InviteSignUpStartResult started, string phone)
    {
        var token = started.TelegramLinkUrl![(started.TelegramLinkUrl.IndexOf('=') + 1)..];
        await using var scope = host.Scope();
        var updates = scope.ServiceProvider.GetRequiredService<TelegramUpdateHandler>();
        await updates.HandleAsync(Update(message => message.Text = "/start " + token));
        await updates.HandleAsync(Update(message => message.Contact = new Contact { PhoneNumber = phone, FirstName = "Lola", UserId = ChatId }));
    }

    private static Update Update(Action<Message> shape)
    {
        var message = new Message
        {
            Id = 1,
            Date = DateTime.UtcNow,
            Chat = new Chat { Id = ChatId, Type = ChatType.Private },
            From = new User { Id = ChatId, IsBot = false, FirstName = "Lola", LanguageCode = "en" },
        };
        shape(message);
        return new Update { Id = 1, Message = message };
    }

    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(300, 300);
        bitmap.Erase(SKColors.Coral);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
