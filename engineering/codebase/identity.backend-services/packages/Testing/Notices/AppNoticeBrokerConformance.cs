using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Identity.Notices;

/// <summary>
/// The app notice broker's conformance cases, run against every implementation: the in-process Module, the Client over the
/// service, and <see cref="FakeAppNoticeBroker"/>. A subclass builds the broker and arranges the people.
/// </summary>
public abstract class AppNoticeBrokerConformance
{
    /// <summary>The broker under test, sending as one app.</summary>
    protected abstract IAppNoticeBroker Broker { get; }

    /// <summary>Creates a person whose confirmed phone is linked to the bot, and returns their <c>sub</c>.</summary>
    protected abstract Task<string> CreateLinkedPersonAsync();

    /// <summary>Creates a person with no Telegram link, and returns their <c>sub</c>.</summary>
    protected abstract Task<string> CreateUnlinkedPersonAsync();

    /// <summary>Mutes the broker's app for the person, as the notice's mute button does.</summary>
    /// <param name="sub">The person's <c>sub</c>.</param>
    protected abstract Task MuteAsync(string sub);

    /// <summary>How many notices reached the person's chat.</summary>
    /// <param name="sub">The person's <c>sub</c>.</param>
    protected abstract Task<int> CountDeliveredAsync(string sub);

    /// <summary>A linked person gets the notice once, however often its key repeats; a repeat answers the first delivery.</summary>
    [Fact]
    public async Task Send_ShouldReachALinkedPerson_OncePerIdempotencyKey()
    {
        var sub = await CreateLinkedPersonAsync();
        var notice = Notice(sub, "<b>Fashion week</b> opens on Monday.");

        var first = Delivered(await Broker.SendNoticeAsync(notice));
        var repeat = Delivered(await Broker.SendNoticeAsync(notice));

        Assert.Equal(AppNoticeStatusConstants.Sent, first.Status);
        Assert.NotNull(first.Id);
        Assert.Equal(first, repeat);
        Assert.Equal(1, await CountDeliveredAsync(sub));
    }

    /// <summary>A key used before with another body is refused, and nothing more goes out.</summary>
    [Fact]
    public async Task Send_ShouldRefuseAReusedKey_WithAnotherBody()
    {
        var sub = await CreateLinkedPersonAsync();
        var notice = Notice(sub, "First text.");
        Assert.Equal(AppNoticeStatusConstants.Sent, Delivered(await Broker.SendNoticeAsync(notice)).Status);

        var reused = await Broker.SendNoticeAsync(notice with { Text = "Another text." });

        Assert.Equal(AppNoticeErrorCodeConstants.IdempotencyKeyReused, CodeOf(reused));
        Assert.Equal(1, await CountDeliveredAsync(sub));
    }

    /// <summary>A person with no Telegram link reads not linked.</summary>
    [Fact]
    public async Task Send_ShouldReadNotLinked_ForAPersonWithNoLink()
    {
        var sub = await CreateUnlinkedPersonAsync();

        var delivery = Delivered(await Broker.SendNoticeAsync(Notice(sub, "Hello.")));

        Assert.Equal(AppNoticeStatusConstants.NotLinked, delivery.Status);
        Assert.Null(delivery.Id);
    }

    /// <summary>A person who muted the app reads muted, and nothing reaches their chat.</summary>
    [Fact]
    public async Task Send_ShouldReadMuted_AndDeliverNothing_ForAPersonWhoMutedTheApp()
    {
        var sub = await CreateLinkedPersonAsync();
        await MuteAsync(sub);

        var delivery = Delivered(await Broker.SendNoticeAsync(Notice(sub, "Hello.")));

        Assert.Equal(AppNoticeStatusConstants.Muted, delivery.Status);
        Assert.Null(delivery.Id);
        Assert.Equal(0, await CountDeliveredAsync(sub));
    }

    /// <summary>A text past the limit, or a button with a relative address, is refused as invalid.</summary>
    [Fact]
    public async Task Send_ShouldRefuseABrokenBody_AsInvalid()
    {
        var sub = await CreateLinkedPersonAsync();

        var tooLong = await Broker.SendNoticeAsync(Notice(sub, new string('a', AppNoticeConstants.MaxTextLength + 1)));
        var badButton = await Broker.SendNoticeAsync(Notice(sub, "Hello.") with { Buttons = [new AppNoticeButton { Text = "Open", Url = "/relative" }] });

        Assert.Equal(AppNoticeErrorCodeConstants.Invalid, CodeOf(tooLong));
        Assert.Equal(AppNoticeErrorCodeConstants.Invalid, CodeOf(badButton));
        Assert.Equal(0, await CountDeliveredAsync(sub));
    }

    /// <summary>A notice to <paramref name="sub"/> with a fresh key and one link button.</summary>
    protected static AppNotice Notice(string sub, string text) => new()
    {
        Subject = sub,
        Text = text,
        Buttons = [new AppNoticeButton { Text = "Open", Url = "https://events.example.test/e/1" }],
        IdempotencyKey = "conformance-" + Guid.NewGuid().ToString("N"),
        Culture = "uz-Latn",
    };

    private static AppNoticeDelivery Delivered(Result<AppNoticeDelivery> result)
    {
        if (result.IsFailure(out var error, out var delivery) || delivery is null)
            Assert.Fail($"The broker failed: {error?.Message}");
        return delivery!;
    }

    private static string? CodeOf(Result<AppNoticeDelivery> result)
    {
        if (result.IsFailure(out var error, out _) && error is not null)
            return error.Metadata?.TryGetValue(AppNoticeConstants.CodeMetadataKey, out var code) == true ? code as string : null;

        Assert.Fail("The broker should have failed.");
        return null;
    }
}
