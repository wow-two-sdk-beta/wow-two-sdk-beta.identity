using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.PhoneSignIn;

/// <summary>
/// The rules under trusted contacts that need no database: a bound code verifies only for its target and shares its
/// subject's window and failure budget; a contact's name is masked to initials; the settings refuse a cap out of range and
/// a switch without the bot.
/// </summary>
public sealed class TrustedContactRuleTests
{
    private const string Contact = "trusted-contact:0190";
    private const string Scope = "identity.trusted-contact";

    [Fact]
    public async Task BoundCode_ShouldVerifyOnlyForItsTarget_AndCountAMismatchAgainstTheSubject()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero));
        await using var provider = new ServiceCollection().AddSingleton<TimeProvider>(time).AddOtpService(o => o.MaxSubjectFailures = 2).BuildServiceProvider();
        var otp = provider.GetRequiredService<IOtpService>();
        var spec = new OtpCodeSpec();

        var forLola = await otp.CreateBoundAsync(Contact, Scope, spec, origin: null, "account:lola");
        (await otp.CreateBoundAsync(Contact, Scope, spec, origin: null, "account:aziz")).FailureReason.Should().Be(OtpFailureReason.RateLimited, "the window is the subject's, whatever the target");
        (await otp.VerifyAsync(Contact, forLola.Code!, Scope)).IsSuccess.Should().BeFalse("a bound code needs its binding");
        (await otp.VerifyBoundAsync(Contact, forLola.Code!, Scope, "account:aziz")).IsSuccess.Should().BeFalse("nor does it serve another target");
        (await otp.VerifyBoundAsync(Contact, forLola.Code!, Scope, "account:lola")).FailureReason.Should().Be(OtpFailureReason.MaxAttemptsReached, "the two mismatches spent the subject's budget of two");

        time.Advance(TimeSpan.FromHours(2));
        var again = await otp.CreateBoundAsync(Contact, Scope, spec, origin: null, "account:lola");
        (await otp.VerifyBoundAsync(Contact, again.Code!, Scope, "account:lola")).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("Sultonbek Rakhimov", "S*** R***")]
    [InlineData("  Maftuna  ", "M***")]
    [InlineData("Лола", "Л***")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Name_ShouldKeepEachWordsInitialOnly(string? name, string? masked)
        => PersonalDataMasks.MaskName(name).Should().Be(masked);

    [Theory]
    [InlineData(0, true, false)]
    [InlineData(11, true, false)]
    [InlineData(3, true, true)]
    [InlineData(3, false, false)]
    public void Settings_ShouldTakeACapOf1To10_AndTheSwitchOnlyWithTheBot(int maxPerPerson, bool isBotOn, bool isValid)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        new IdentityBuilder(services)
            .AddTelegramLinks(o =>
            {
                o.IsEnabled = isBotOn;
                o.BotToken = "123456:TEST-token";
                o.BotUserName = "ocharo_bot";
                o.UpdateMode = TelegramUpdateMode.Polling;
            })
            .AddTrustedContacts(o =>
            {
                o.IsEnabled = true;
                o.MaxPerPerson = maxPerPerson;
            });
        using var provider = services.BuildServiceProvider();

        var read = () => provider.GetRequiredService<IOptionsMonitor<TrustedContactOptions>>().CurrentValue;

        if (isValid)
            read.Should().NotThrow();
        else
            read.Should().Throw<OptionsValidationException>().Which.Message.Should().Contain(isBotOn ? "MaxPerPerson" : "Identity:TrustedContacts:IsEnabled");
    }
}
