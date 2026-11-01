using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Comms.Email;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;
using WoW.Two.Sdk.Beta.Be.Testing;

namespace Identity.Tests.E2E.Accounts;

/// <summary>
/// The real API over a throwaway PostgreSQL database, its email broker replaced by one that keeps what it is asked to send,
/// and its clock at the real time. Sign-up is open and the issuer is set, so the emails carry links to the hosted pages.
/// </summary>
public sealed class AccountHost : IAsyncDisposable
{
    /// <summary>The issuer the host is configured with; the emails' links start at its origin.</summary>
    public const string Issuer = "https://id.example.test/";

    /// <summary>A password that meets the default rules.</summary>
    public const string StrongPassword = "correct horse battery";

    private readonly string _connectionString;
    private readonly bool _ownsDatabase;

    /// <summary>Create the host; <paramref name="settings"/> lay over the defaults, and <paramref name="connectionString"/> shares another host's database.</summary>
    /// <param name="settings">Extra host settings.</param>
    /// <param name="connectionString">A database an earlier host made; null makes a new one.</param>
    /// <param name="keepRealBroker">Whether the email broker stays the instance's own, to see what it does without SMTP.</param>
    public AccountHost(IReadOnlyDictionary<string, string?>? settings = null, string? connectionString = null, bool keepRealBroker = false)
    {
        _ownsDatabase = connectionString is null;
        _connectionString = connectionString ?? Task.Run(PostgresTestServer.CreateDatabaseAsync).GetAwaiter().GetResult();
        var values = new Dictionary<string, string?>
        {
            ["DatabaseSettings:ConnectionString"] = _connectionString,
            ["AllowedHosts"] = "localhost",
            ["Logging:LogLevel:Default"] = "Warning",
            ["Identity:SignUp:IsEnabled"] = "true",
            ["Identity:Server:Issuer"] = Issuer,
            ["Identity:Otp:RateLimitWindow"] = "00:00:00",
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
            values[key] = value;

        Host = new WebApiTestHost<Program>
        {
            ConfigureConfigurationHook = configuration => configuration.AddInMemoryCollection(values),
            ConfigureServicesHook = services =>
            {
                if (keepRealBroker)
                    return;

                services.RemoveAll<IEmailBroker>();
                services.AddSingleton<IEmailBroker>(Mail);
            },
        };
        Host.Clock.SetUtcNow(DateTimeOffset.UtcNow);
        using var client = Host.CreateClient();
    }

    /// <summary>Gets the API host; its <see cref="WebApiTestHost{TEntryPoint}.Clock"/> is the server's clock.</summary>
    public WebApiTestHost<Program> Host { get; }

    /// <summary>Gets the connection string of the host's database.</summary>
    public string ConnectionString => _connectionString;

    /// <summary>Gets the emails the host was asked to send.</summary>
    public CapturingMail Mail { get; } = new();

    /// <summary>A browser-like client: HTTPS, cookies kept, redirects returned rather than followed.</summary>
    public HttpClient CreateClient()
        => Host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    /// <summary>Create an account with a password directly in the store; returns its id.</summary>
    /// <param name="email">The email, also the user name.</param>
    /// <param name="password">The password; it must meet this host's rules.</param>
    /// <param name="isEmailConfirmed">Whether the email counts as confirmed.</param>
    public async Task<Guid> CreateUserAsync(string email, string password = StrongPassword, bool isEmailConfirmed = true)
    {
        await using var scope = Host.Services.CreateAsyncScope();
        var user = new IdentityUser { UserName = email, Email = email, IsEmailConfirmed = isEmailConfirmed };
        var created = await scope.ServiceProvider.GetRequiredService<UserPasswordService>().CreateWithPasswordAsync(user, password);
        if (!created.IsSuccess)
            throw new InvalidOperationException(string.Join("; ", created.Errors.Select(error => error.Code)));

        return user.Id;
    }

    /// <summary>Reload an account as the next request would see it.</summary>
    /// <param name="id">The account.</param>
    public async Task<IdentityUser> FindAsync(Guid id)
    {
        await using var scope = Host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(id)
            ?? throw new InvalidOperationException($"No account {id}.");
    }

    /// <summary>Sign in with a session cookie; returns the response.</summary>
    /// <param name="client">The client that keeps the cookie.</param>
    /// <param name="login">The login.</param>
    /// <param name="password">The password.</param>
    public static Task<HttpResponseMessage> SignInAsync(HttpClient client, string login, string password)
        => client.PostAsJsonAsync("/api/account/login?useCookies=true", new { login, password });

    /// <summary>The <c>data</c> of an enveloped success body.</summary>
    /// <param name="response">The response.</param>
    public static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("data").Clone();
    }

    /// <summary>The body of a problem response.</summary>
    /// <param name="response">The response.</param>
    public static async Task<JsonElement> ProblemAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        if (_ownsDatabase)
            await PostgresTestServer.DropDatabaseAsync(_connectionString);
    }
}

/// <summary>An email broker that keeps every message it is asked to send, and accepts it.</summary>
public sealed class CapturingMail : IEmailBroker
{
    private readonly List<EmailMessage> _sent = [];

    /// <summary>Gets the messages sent so far.</summary>
    public IReadOnlyList<EmailMessage> Sent
    {
        get
        {
            lock (_sent)
                return [.. _sent];
        }
    }

    /// <inheritdoc />
    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        lock (_sent)
            _sent.Add(message);

        return Task.FromResult(new EmailSendResult { Success = true });
    }

    /// <summary>The first link in the latest message to <paramref name="address"/> with <paramref name="subject"/>.</summary>
    /// <param name="subject">The subject.</param>
    /// <param name="address">The recipient.</param>
    public Uri LinkOf(string subject, string address)
    {
        var message = Sent.Last(sent => sent.Subject == subject && sent.To.Any(to => to.Address == address));
        var start = message.TextBody!.IndexOf("https://", StringComparison.Ordinal);
        var end = message.TextBody.IndexOfAny([' ', '\n', '\r'], start);
        return new Uri(message.TextBody[start..(end < 0 ? message.TextBody.Length : end)]);
    }
}
