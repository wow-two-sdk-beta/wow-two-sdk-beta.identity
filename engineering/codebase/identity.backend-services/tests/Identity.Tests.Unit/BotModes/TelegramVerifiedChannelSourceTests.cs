using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Channels;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Emails;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.BotModes;

/// <summary>A confirmed phone number linked to a chat of the current bot is a Telegram channel; nothing else is.</summary>
public sealed class TelegramVerifiedChannelSourceTests
{
    private const string Phone = "+998901234567";
    private const string Bot = "ocharo_bot";

    [Fact]
    public async Task Source_ShouldListTheLinkedChat_ForAConfirmedNumber()
    {
        using var host = TelegramTestHost.Create();
        await LinkAsync(host, Phone, chatId: 4242);

        var channels = await ListAsync(host, new IdentityUser { PhoneNumber = Phone, IsPhoneNumberConfirmed = true });

        channels.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Channel = OtpChannelNameConstants.Telegram, Address = "4242" });
    }

    [Fact]
    public async Task Source_ShouldReadAFormattedNumber_AsItsE164Link()
    {
        using var host = TelegramTestHost.Create();
        await LinkAsync(host, Phone, chatId: 4242);

        var channels = await ListAsync(host, new IdentityUser { PhoneNumber = "+998 90 123-45-67", IsPhoneNumberConfirmed = true });

        channels.Should().ContainSingle().Which.Address.Should().Be("4242");
    }

    [Fact]
    public async Task Source_ShouldListNothing_ForAnUnconfirmedMissingOrUnlinkedNumber()
    {
        using var host = TelegramTestHost.Create();
        await LinkAsync(host, Phone, chatId: 4242);

        (await ListAsync(host, new IdentityUser { PhoneNumber = Phone, IsPhoneNumberConfirmed = false })).Should().BeEmpty("an unconfirmed number proves nothing");
        (await ListAsync(host, new IdentityUser { PhoneNumber = null, IsPhoneNumberConfirmed = true })).Should().BeEmpty();
        (await ListAsync(host, new IdentityUser { PhoneNumber = "not a number", IsPhoneNumberConfirmed = true })).Should().BeEmpty();
        (await ListAsync(host, new IdentityUser { PhoneNumber = "+998907654321", IsPhoneNumberConfirmed = true })).Should().BeEmpty("that number never linked a chat");
    }

    [Fact]
    public async Task Source_ShouldListNothing_ForAChatLinkedThroughAnotherBot()
    {
        using var host = TelegramTestHost.Create();
        await host.Links.SaveLinkAsync(new IdentityTelegramLink { Id = Guid.NewGuid(), BotUserName = "old_bot", PhoneNumber = Phone, ChatId = 1, TelegramUserId = 1, LinkedAt = DateTimeOffset.UtcNow });

        (await ListAsync(host, new IdentityUser { PhoneNumber = Phone, IsPhoneNumberConfirmed = true })).Should().BeEmpty("replacing the bot starts with no links");
    }

    [Fact]
    public async Task Source_ShouldListNothing_WhileTheBotIsOff()
    {
        using var host = TelegramTestHost.Create(bot => bot.IsEnabled = false);
        await LinkAsync(host, Phone, chatId: 4242);

        (await ListAsync(host, new IdentityUser { PhoneNumber = Phone, IsPhoneNumberConfirmed = true })).Should().BeEmpty();
    }

    [Fact]
    public void Registration_ShouldPutTelegramAfterEmail_AndOnlyOnce()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITelegramLinkRepository>(new InMemoryTelegramLinkRepository());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IUserVerifiedChannelSource, EmailVerifiedChannelSource>());
        var identity = new IdentityBuilder(services);
        identity.AddTelegramLinks(bot => bot.IsEnabled = false);
        identity.AddTelegramLinks();
        using var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetServices<IUserVerifiedChannelSource>().Select(source => source.GetType()).Should()
            .Equal(typeof(EmailVerifiedChannelSource), typeof(TelegramVerifiedChannelSource));
    }

    private static async Task LinkAsync(TelegramTestHost host, string phone, long chatId)
        => await host.Links.SaveLinkAsync(new IdentityTelegramLink
        {
            Id = Guid.NewGuid(),
            BotUserName = Bot,
            PhoneNumber = phone,
            ChatId = chatId,
            TelegramUserId = chatId,
            LinkedAt = DateTimeOffset.UtcNow,
        });

    private static async Task<IReadOnlyList<VerifiedChannel>> ListAsync(TelegramTestHost host, IdentityUser user)
    {
        await using var scope = host.Provider.CreateAsyncScope();
        var source = scope.ServiceProvider.GetServices<IUserVerifiedChannelSource>().OfType<TelegramVerifiedChannelSource>().Single();
        return await source.ListAsync(user);
    }
}
