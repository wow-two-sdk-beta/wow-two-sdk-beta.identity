using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Phones;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.PhoneSignIn;

/// <summary>Codes kept as digests, attempts claimed before comparing, limits per subject and origin, the phone rules and the bot's boot check.</summary>
public sealed class OtpStoreAndPhoneRulesTests
{
    private const string Phone = "+998901234567";
    private const string Scope = "identity.phone-sign-in";

    [Fact]
    public async Task Store_ShouldHoldDigestsOnly_AndSpendACodeOnce()
    {
        var (provider, _) = CreateOtp();
        await using var _provider = provider;
        await using var scope = provider.CreateAsyncScope();
        var otp = scope.ServiceProvider.GetRequiredService<IOtpService>();
        var store = scope.ServiceProvider.GetRequiredService<IOtpRepository>();

        var created = await otp.CreateAsync(Phone, Scope);
        var hasher = provider.GetRequiredService<OtpCodeHasher>();
        var record = (await store.FindLatestPendingAsync(hasher.Subject(Phone), Scope))!;

        record.SubjectHash.Should().NotContain("998901234567").And.HaveLength(64);
        record.CodeHash.Should().NotContain(created.Code!).And.HaveLength(64);
        (await otp.VerifyAsync(Phone, created.Code!, Scope)).IsSuccess.Should().BeTrue();
        (await otp.VerifyAsync(Phone, created.Code!, Scope)).FailureReason.Should().Be(OtpFailureReason.InvalidCode, "a spent code never verifies again");
    }

    [Fact]
    public async Task Verify_ShouldLockTheCode_OnceItsAttemptsAreSpent()
    {
        var (provider, _) = CreateOtp(o => o.MaxAttempts = 3);
        await using var _provider = provider;
        await using var scope = provider.CreateAsyncScope();
        var otp = scope.ServiceProvider.GetRequiredService<IOtpService>();
        var created = await otp.CreateAsync(Phone, Scope);
        var wrong = created.Code == "000000" ? "111111" : "000000";

        for (var attempt = 0; attempt < 3; attempt++)
            (await otp.VerifyAsync(Phone, wrong, Scope)).FailureReason.Should().Be(OtpFailureReason.InvalidCode);

        (await otp.VerifyAsync(Phone, created.Code!, Scope)).FailureReason.Should().Be(OtpFailureReason.MaxAttemptsReached, "the right code comes too late");
    }

    [Fact]
    public async Task Create_ShouldLimitBySubjectAndByOrigin_WithinTheWindow()
    {
        var (provider, time) = CreateOtp(o => o.RateLimitWindow = TimeSpan.FromSeconds(30));
        await using var _provider = provider;
        await using var scope = provider.CreateAsyncScope();
        var otp = scope.ServiceProvider.GetRequiredService<IOtpService>();
        var spec = new OtpCodeSpec();

        (await otp.CreateAsync(Phone, Scope, spec, "203.0.113.5")).IsSuccess.Should().BeTrue();
        (await otp.CreateAsync(Phone, Scope, spec, "198.51.100.7")).FailureReason.Should().Be(OtpFailureReason.RateLimited, "the number asked moments ago");
        (await otp.CreateAsync("+998907654321", Scope, spec, "203.0.113.5")).FailureReason.Should().Be(OtpFailureReason.RateLimited, "the address asked moments ago");
        (await otp.IsRateLimitedAsync("+998907654321", Scope, "198.51.100.7")).Should().BeFalse();

        time.Advance(TimeSpan.FromSeconds(31));
        (await otp.CreateAsync("+998907654321", Scope, spec, "203.0.113.5")).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Verify_ShouldShareOneFailureBudgetAcrossCodes_SoANewCodeBringsNoFreshGuesses()
    {
        var (provider, time) = CreateOtp(o =>
        {
            o.RateLimitWindow = TimeSpan.Zero;
            o.MaxAttempts = 5;
            o.MaxSubjectFailures = 7;
        });
        await using var _provider = provider;
        await using var scope = provider.CreateAsyncScope();
        var otp = scope.ServiceProvider.GetRequiredService<IOtpService>();

        var first = await otp.CreateAsync(Phone, Scope);
        for (var attempt = 0; attempt < 5; attempt++)
            (await otp.VerifyAsync(Phone, WrongFor(first.Code!), Scope)).IsSuccess.Should().BeFalse();
        time.Advance(TimeSpan.FromSeconds(1));
        var second = await otp.CreateAsync(Phone, Scope);
        second.IsSuccess.Should().BeTrue("five failures stay inside the budget of seven");
        (await otp.VerifyAsync(Phone, WrongFor(second.Code!), Scope)).FailureReason.Should().Be(OtpFailureReason.InvalidCode);
        (await otp.VerifyAsync(Phone, WrongFor(second.Code!), Scope)).FailureReason.Should().Be(OtpFailureReason.InvalidCode);

        (await otp.VerifyAsync(Phone, second.Code!, Scope)).FailureReason.Should().Be(OtpFailureReason.MaxAttemptsReached, "seven wrong guesses across both codes spent the budget");
        time.Advance(TimeSpan.FromSeconds(1));
        (await otp.CreateAsync(Phone, Scope)).FailureReason.Should().Be(OtpFailureReason.RateLimited, "no new code is issued past the budget");
        (await otp.IsRateLimitedAsync(Phone, Scope, origin: null)).Should().BeTrue();

        time.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));
        var fresh = await otp.CreateAsync(Phone, Scope);
        fresh.IsSuccess.Should().BeTrue("the failures left the window");
        (await otp.VerifyAsync(Phone, fresh.Code!, Scope)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Verify_ShouldTakeOnlyTheLatestCode_SoASupersededCodeNeverComesBack()
    {
        var (provider, time) = CreateOtp(o => o.RateLimitWindow = TimeSpan.Zero);
        await using var _provider = provider;
        await using var scope = provider.CreateAsyncScope();
        var otp = scope.ServiceProvider.GetRequiredService<IOtpService>();

        var older = await otp.CreateAsync(Phone, Scope);
        time.Advance(TimeSpan.FromSeconds(1));
        var newer = await otp.CreateAsync(Phone, Scope);
        (await otp.VerifyAsync(Phone, newer.Code!, Scope)).IsSuccess.Should().BeTrue();

        if (older.Code != newer.Code)
            (await otp.VerifyAsync(Phone, older.Code!, Scope)).IsSuccess.Should().BeFalse("spending the newer code must not revive the one it replaced");
    }

    [Fact]
    public async Task Discard_ShouldRemoveAnUndeliveredCode()
    {
        var (provider, _) = CreateOtp();
        await using var _provider = provider;
        await using var scope = provider.CreateAsyncScope();
        var otp = scope.ServiceProvider.GetRequiredService<IOtpService>();

        var created = await otp.CreateAsync(Phone, Scope);
        created.CodeId.Should().NotBeNull();
        await otp.DiscardAsync(created.CodeId!.Value);

        (await otp.VerifyAsync(Phone, created.Code!, Scope)).FailureReason.Should().Be(OtpFailureReason.InvalidCode, "a discarded code is gone");
        (await otp.IsRateLimitedAsync(Phone, Scope, origin: null)).Should().BeFalse("nothing went out, so nothing holds the next send back");
    }

    [Fact]
    public async Task HashKey_ShouldBeShared_SoAnotherProcessVerifiesTheCode()
    {
        const string key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
        var shared = new MemoryOtpRepository(TimeProvider.System);
        var (first, _) = CreateOtp(o => o.HashKey = key, shared);
        var (second, _) = CreateOtp(o => o.HashKey = key, shared);
        var (stranger, _) = CreateOtp(store: shared);
        await using var _first = first;
        await using var _second = second;
        await using var _stranger = stranger;

        var created = await first.GetRequiredService<IOtpService>().CreateAsync(Phone, Scope);

        (await stranger.GetRequiredService<IOtpService>().VerifyAsync(Phone, created.Code!, Scope)).IsSuccess.Should().BeFalse("its own key finds no code");
        (await second.GetRequiredService<IOtpService>().VerifyAsync(Phone, created.Code!, Scope)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void HashKey_ShouldStopTheStart_WhenShorterThan32Bytes()
    {
        using var provider = new ServiceCollection().AddOtpService(o => o.HashKey = Convert.ToBase64String(new byte[16])).BuildServiceProvider();

        FluentActions.Invoking(() => provider.GetRequiredService<OtpOptions>()).Should().Throw<OptionsValidationException>();
    }

    [Theory]
    [InlineData("+998 90 123-45-67", "+998901234567")]
    [InlineData("00998901234567", "+998901234567")]
    [InlineData("(+998) 90.123.45.67", "+998901234567")]
    [InlineData("998901234567", null)]
    [InlineData("+0998901234", null)]
    [InlineData("+99890", null)]
    [InlineData("+9989012345671234", null)]
    [InlineData("+99890abc4567", null)]
    public void Normalize_ShouldReadE164Only(string typed, string? expected)
        => PhoneNumberPolicy.Normalize(typed).Should().Be(expected);

    [Fact]
    public void Countries_ShouldFollowTheLongestDiallingCode_AndTheAllowedList()
    {
        PhoneNumberPolicy.CountriesOf("+998901234567").Should().Equal("UZ");
        PhoneNumberPolicy.CountriesOf("+77011234567").Should().BeEquivalentTo("RU", "KZ");
        PhoneNumberPolicy.CountriesOf("+380441234567").Should().Equal("UA");
        PhoneNumberPolicy.CountriesOf("+2991234567").Should().BeEmpty("no country in the list dials +299");
        PhoneNumberPolicy.NormalizeContact("998901234567").Should().Be("+998901234567");

        var uzbekOnly = Policy(o => o.AllowedCountries = ["uz"]);
        uzbekOnly.IsAllowed("+998901234567").Should().BeTrue();
        uzbekOnly.IsAllowed("+77011234567").Should().BeFalse();
        Policy(null).IsAllowed("+77011234567").Should().BeTrue("an empty list allows every known country");
        Policy(null).IsAllowed("+2991234567").Should().BeFalse();
    }

    [Fact]
    public void AllowedCountries_ShouldStopTheStart_ForAnUnknownCode()
    {
        var services = new ServiceCollection();
        services.AddUserAccounts().AddPhoneNumbers(o => o.AllowedCountries = ["UZ", "XX"]);
        using var provider = services.BuildServiceProvider();

        FluentActions.Invoking(() => provider.GetRequiredService<PhoneOptions>()).Should().Throw<OptionsValidationException>().WithMessage("*AllowedCountries*");
    }

    [Fact]
    public void Bot_ShouldStopTheStart_WhenOnWithoutItsToken_AndPassWhenOff()
    {
        var problems = Validate(o =>
        {
            o.IsEnabled = true;
            o.BotUserName = "ocharo_bot";
            o.WebhookSecret = "bad secret!";
        });

        problems.Should().Contain(p => p.Contains("BotToken")).And.Contain(p => p.Contains("WebhookSecret"));
        Validate(o => o.IsEnabled = false).Should().BeEmpty();
        Validate(o =>
        {
            o.IsEnabled = true;
            o.BotToken = "123456:ABC-def_ghi";
            o.BotUserName = "@Ocharo_Bot";
            o.WebhookSecret = "hook-secret_1";
        }).Should().BeEmpty();
    }

    [Fact]
    public void Bot_ShouldNeedTheSharedHashKey_OnADeployedBoot()
    {
        void Complete(TelegramBotOptions o)
        {
            o.IsEnabled = true;
            o.BotToken = "123456:ABC-def_ghi";
            o.BotUserName = "ocharo_bot";
            o.WebhookSecret = "hook-secret_1";
        }

        Validate(Complete, environment: "Production").Should().ContainSingle().Which.Should().Contain("Identity:Otp:HashKey");
        Validate(Complete, environment: "Production", hashKey: "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=").Should().BeEmpty();
        Validate(Complete, environment: "Development").Should().BeEmpty("a local run keeps its own key");
    }

    private static IReadOnlyList<string> Validate(Action<TelegramBotOptions> configure, string? environment = null, string? hashKey = null)
    {
        var services = new ServiceCollection();
        if (environment is not null)
        {
            services.AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = environment, ApplicationName = "Identity.Api", ContentRootPath = AppContext.BaseDirectory });
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        }

        services.AddOtpService(o => o.HashKey = hashKey);
        services.AddUserAccounts().AddTelegramLinks(configure);
        using var provider = services.BuildServiceProvider();
        try
        {
            _ = provider.GetRequiredService<IOptionsMonitor<TelegramBotOptions>>().CurrentValue;
            return [];
        }
        catch (OptionsValidationException exception)
        {
            return [.. exception.Failures];
        }
    }

    private static string WrongFor(string code) => code == "000000" ? "111111" : "000000";

    private static PhoneNumberPolicy Policy(Action<PhoneOptions>? configure)
    {
        var options = new PhoneOptions();
        configure?.Invoke(options);
        return new PhoneNumberPolicy(new StaticMonitor<PhoneOptions>(options));
    }

    private static (ServiceProvider Provider, FakeTimeProvider Time) CreateOtp(Action<OtpOptions>? configure = null, IOtpRepository? store = null)
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero));
        var services = new ServiceCollection().AddSingleton<TimeProvider>(time);
        if (store is not null)
            services.AddSingleton(store);
        return (services.AddOtpService(configure).BuildServiceProvider(), time);
    }

    private sealed class StaticMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
