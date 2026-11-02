using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Telegram.Bot.Exceptions;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;
using WoW.Two.Sdk.Beta.Be.Identity.Otp.Models;
using WoW.Two.Sdk.Beta.Be.Identity.Otp.Telegram;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.BotModes;

/// <summary>
/// Code messages of the <c>telegram</c> channel: each one the instance's bot sends is recorded due when its code expires,
/// so the sweep deletes it then; a refused send records nothing, and a host with its own bot and no recorder still sends.
/// </summary>
public sealed class TelegramCodeMessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CodeMessage_ShouldBeRecorded_AndDeletedOnceTheCodeExpires()
    {
        var time = new FakeTimeProvider(Now);
        using var host = TelegramTestHost.Create(time: time);

        (await SendAsync(host, Envelope("501", lifetime: TimeSpan.FromMinutes(3), culture: "uz"))).IsSuccess.Should().BeTrue();

        host.Bot.Sent.Should().ContainSingle().Which.ChatId.Identifier.Should().Be(501);
        var record = host.Ledger.Messages.Should().ContainSingle().Which;
        record.Should().BeEquivalentTo(new
        {
            BotUserName = "ocharo_bot",
            ChatId = 501L,
            Kind = TelegramMessageKindConstants.Code,
            UserId = (Guid?)null,
            Culture = "uz",
            SentAt = Now,
            DeleteAt = Now.AddMinutes(3),
        });

        time.Advance(TimeSpan.FromMinutes(3) - TimeSpan.FromSeconds(1));
        (await host.SweepAsync()).Should().Be(0, "the code still works");
        time.Advance(TimeSpan.FromSeconds(1));
        (await host.SweepAsync()).Should().Be(1);
        var deletion = host.Bot.Deletions.Should().ContainSingle().Which;
        deletion.ChatId.Identifier.Should().Be(501);
        deletion.MessageIds.Should().Equal(record.MessageId);
        host.Ledger.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task CodeMessage_ShouldTakeTheOtpCodeLifetime_WhenTheEnvelopeNamesNone()
    {
        using var host = TelegramTestHost.Create(time: new FakeTimeProvider(Now));
        var lifetime = host.Provider.GetRequiredService<OtpOptions>().CodeLifetime;

        await SendAsync(host, Envelope("501", lifetime: null, culture: null));

        host.Ledger.Messages.Should().ContainSingle().Which.DeleteAt.Should().Be(Now + lifetime);
    }

    [Fact]
    public async Task CodeMessage_ShouldRecordNothing_WhenTelegramRefusesIt()
    {
        using var host = TelegramTestHost.Create(time: new FakeTimeProvider(Now));
        host.Bot.ChatFailures[501] = new ApiRequestException("Forbidden: bot was blocked by the user", 403);

        (await SendAsync(host, Envelope("501", lifetime: null, culture: null))).IsSuccess.Should().BeFalse();

        host.Ledger.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task CodeMessage_ShouldStillBeSent_ByAHandlerWithNoRecorder()
    {
        var bot = new ScriptedTelegramBotClient();
        var handler = new TelegramOtpDeliveryHandler(bot, new TelegramOtpOptions(), new OtpOptions());

        (await handler.SendAsync(Envelope("501", lifetime: null, culture: null))).IsSuccess.Should().BeTrue();

        bot.Sent.Should().ContainSingle();
    }

    private static OtpDeliveryEnvelopeModel Envelope(string chat, TimeSpan? lifetime, string? culture)
        => new() { DeliveryAddress = chat, Code = "123456", Scope = "identity.phone-sign-in", Lifetime = lifetime, Culture = culture };

    private static async Task<OtpDeliveryResult> SendAsync(TelegramTestHost host, OtpDeliveryEnvelopeModel envelope)
    {
        await using var scope = host.Provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredKeyedService<IOtpDeliveryHandler>(OtpChannelNameConstants.Telegram).SendAsync(envelope);
    }
}
