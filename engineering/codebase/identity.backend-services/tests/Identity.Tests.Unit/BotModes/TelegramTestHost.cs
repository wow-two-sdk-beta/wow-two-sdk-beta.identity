using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Otp;
using WoW.Two.Sdk.Beta.Be.Identity.Telegram;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.BotModes;

/// <summary>
/// The bot's registration over a scripted Telegram, in-memory links, an in-memory record of who holds which number
/// confirmed and a captured log, with a chosen host environment and no database.
/// </summary>
internal sealed class TelegramTestHost : IDisposable
{
    /// <summary>A bot token that passes the boot check.</summary>
    public const string Token = "123456:TEST-token";

    private TelegramTestHost(ServiceProvider provider, ScriptedTelegramBotClient bot, InMemoryTelegramLinkRepository links, PhoneHolderUserRepository users, CapturedLog log)
    {
        Provider = provider;
        Bot = bot;
        Links = links;
        Users = users;
        Log = log;
    }

    public ServiceProvider Provider { get; }

    public ScriptedTelegramBotClient Bot { get; }

    public InMemoryTelegramLinkRepository Links { get; }

    /// <summary>Who holds which number confirmed.</summary>
    public PhoneHolderUserRepository Users { get; }

    public CapturedLog Log { get; }

    /// <summary>Builds the host.</summary>
    /// <param name="bot">Configures the bot; the token and user name are set already.</param>
    /// <param name="environment">The host environment, <c>Production</c> for a deployed boot or <c>Development</c> for a local run.</param>
    /// <param name="withoutBotClient">Leaves the bot client to the registration's own, which needs a token.</param>
    /// <param name="schedule">The polling pace; short by default so a test never waits.</param>
    /// <param name="configuration">Host settings, such as <c>DEPLOY_ENVIRONMENT</c>.</param>
    /// <param name="hasHashKey">Whether <c>Identity:Otp:HashKey</c> is set, which a deployed boot with the bot on requires.</param>
    /// <param name="time">The clock; the system's when null.</param>
    /// <param name="services">Adds services before the bot's registration, such as a completion handler.</param>
    public static TelegramTestHost Create(
        Action<TelegramBotOptions>? bot = null,
        string environment = "Production",
        bool withoutBotClient = false,
        TelegramPollingSchedule? schedule = null,
        IReadOnlyDictionary<string, string?>? configuration = null,
        bool hasHashKey = true,
        TimeProvider? time = null,
        Action<IServiceCollection>? services = null)
    {
        var scripted = new ScriptedTelegramBotClient();
        var links = new InMemoryTelegramLinkRepository();
        var users = new PhoneHolderUserRepository();
        var log = new CapturedLog();
        var collection = new ServiceCollection();
        collection.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Information).AddProvider(log));
        collection.AddSingleton<IHostEnvironment>(new TestEnvironment(environment));
        collection.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configuration ?? new Dictionary<string, string?>()).Build());
        collection.AddSingleton(schedule ?? new TelegramPollingSchedule
        {
            MinBackoff = TimeSpan.FromMilliseconds(5),
            MaxBackoff = TimeSpan.FromMilliseconds(20),
            IdleDelay = TimeSpan.FromMilliseconds(5),
        });
        if (!withoutBotClient)
            collection.AddSingleton<ITelegramBotClient>(scripted);
        collection.AddSingleton<ITelegramLinkRepository>(links);
        collection.AddSingleton<IUserRepository>(users);
        if (time is not null)
            collection.AddSingleton(time);
        services?.Invoke(collection);
        if (hasHashKey)
            collection.AddOtpService(otp => otp.HashKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=");
        new IdentityBuilder(collection).AddTelegramLinks(options =>
        {
            options.IsEnabled = true;
            options.BotToken = Token;
            options.BotUserName = "Ocharo_Bot";
            options.WebhookSecret = "hook-secret";
            bot?.Invoke(options);
        });
        return new TelegramTestHost(collection.BuildServiceProvider(), scripted, links, users, log);
    }

    /// <summary>The hosted service of type <typeparamref name="T"/>.</summary>
    public T Service<T>() => Provider.GetServices<IHostedService>().OfType<T>().Single();

    public void Dispose() => Provider.Dispose();

    /// <summary>Waits until <paramref name="condition"/> holds, up to ten seconds.</summary>
    public static async Task EventuallyAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The condition did not hold within ten seconds.");

            await Task.Delay(10);
        }
    }

    /// <summary>A host environment with a chosen name.</summary>
    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>Keeps every log entry so a test can count what was reported.</summary>
    public sealed class CapturedLog : ILoggerProvider
    {
        private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

        public IReadOnlyList<(LogLevel Level, string Message)> Entries => [.. _entries];

        public ILogger CreateLogger(string categoryName) => new Sink(_entries);

        public void Dispose()
        {
        }

        private sealed class Sink(ConcurrentQueue<(LogLevel Level, string Message)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => entries.Enqueue((logLevel, formatter(state, exception)));
        }
    }
}
