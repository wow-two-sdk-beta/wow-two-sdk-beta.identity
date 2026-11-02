using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Beta.Be.Identity.Cookies;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SecurityStamps;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignIn;
using WoW.Two.Sdk.Beta.Be.Meta;
using SessionOptions = WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions.SessionOptions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Accounts;

/// <summary>
/// How long a cookie session lasts: a fixed session takes <c>Lifetime</c> with a browser-session cookie, or
/// <c>ExtendedLifetime</c> with a lasting cookie when the sign-in asked to stay signed in; a sliding session moves its end
/// with each use and reissues its cookie.
/// </summary>
public sealed class SessionLifetimeTests
{
    private const string Password = "correct horse battery";
    private const string Email = "fern@example.test";

    [Fact]
    public async Task FixedSession_ShouldEndWithTheBrowser_UnlessTheSignInAskedToStay()
    {
        await using var app = await StartAsync(_ => { });
        var userId = await app.SignUpAsync(Email);
        using var shared = app.CreateClient();
        using var own = app.CreateClient();

        var brief = await app.SignInAsync(shared, isPersistent: false);
        brief.Should().NotContain("expires=", "a session cookie ends with the browser");
        var kept = await app.SignInAsync(own, isPersistent: true);
        ExpiryOf(kept).Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(30), TimeSpan.FromMinutes(1));

        var ends = (await app.SessionsAsync(userId)).OrderBy(session => session.ExpiresAt).Select(session => session.ExpiresAt - session.CreatedAt).ToList();
        ends.Should().Equal(TimeSpan.FromHours(12), TimeSpan.FromDays(30));
    }

    [Fact]
    public async Task FixedSession_ShouldIgnoreTheAskToStay_WhenNoExtendedLifetimeIsSet()
    {
        await using var app = await StartAsync(options => options.ExtendedLifetime = null);
        var userId = await app.SignUpAsync(Email);
        using var browser = app.CreateClient();

        (await app.SignInAsync(browser, isPersistent: true)).Should().NotContain("expires=");
        var session = (await app.SessionsAsync(userId)).Single();
        (session.ExpiresAt - session.CreatedAt).Should().Be(TimeSpan.FromHours(12));
    }

    [Fact]
    public async Task SlidingSession_ShouldMoveItsEndWithUse_AndReissueALastingCookie()
    {
        await using var app = await StartAsync(options =>
        {
            options.Expiration = SessionExpiration.Sliding;
            options.SlidingLifetime = TimeSpan.FromDays(5);
            options.ActivityInterval = TimeSpan.Zero;
        });
        var userId = await app.SignUpAsync(Email);
        using var browser = app.CreateClient();

        var first = await app.SignInAsync(browser, isPersistent: false);
        ExpiryOf(first).Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(5), TimeSpan.FromMinutes(1), "a sliding session's cookie outlives the browser");
        var signedInEnd = (await app.SessionsAsync(userId)).Single().ExpiresAt;

        await Task.Delay(TimeSpan.FromMilliseconds(20));
        using var used = await browser.GetAsync("/account/manage/info");
        used.StatusCode.Should().Be(HttpStatusCode.OK);
        used.Headers.TryGetValues("Set-Cookie", out var reissued).Should().BeTrue("the moved end reissues the cookie");
        ExpiryOf(reissued!.Last()).Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(5), TimeSpan.FromMinutes(1));
        (await app.SessionsAsync(userId)).Single().ExpiresAt.Should().BeAfter(signedInEnd);
    }

    [Fact]
    public async Task Settings_ShouldRefuseAnExtendedLifetimeNoLongerThanLifetime()
    {
        var start = () => StartAsync(options => options.ExtendedLifetime = TimeSpan.FromHours(12));
        (await start.Should().ThrowAsync<Microsoft.Extensions.Options.OptionsValidationException>())
            .Which.Message.Should().Contain("ExtendedLifetime");
    }

    private static DateTimeOffset ExpiryOf(string setCookie)
    {
        var attribute = setCookie.Split(';').Select(part => part.Trim()).Single(part => part.StartsWith("expires=", StringComparison.OrdinalIgnoreCase));
        return DateTimeOffset.Parse(attribute["expires=".Length..], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<LifetimeApp> StartAsync(Action<SessionOptions> configure)
    {
        var database = new IdentityTestDatabase();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.AddApiDefaults(options =>
        {
            options.EnableHttpsRedirection = false;
            options.EnableOtlpExporters = false;
            options.ExposeOpenApi = false;
            options.EnableRateLimiting = false;
        });
        database.Register(builder.Services);
        builder.Services.AddUserAccounts()
            .AddArgon2Passwords()
            .AddSecurityStampValidation(o => o.ValidationInterval = TimeSpan.Zero)
            .AddSessions(configure)
            .AddSignIn()
            .AddAccountEndpoints(o => o.Mode = SignUpMode.Open);
        builder.Services.AddCookieAuthentication(o =>
        {
            o.Mode = AuthChallengeMode.Api;
            o.IsExpirationSliding = false;
        });
        builder.Services.AddAuthorization();

        var web = builder.Build();
        web.UseApiDefaults(pipeline =>
        {
            pipeline.UseAuthentication();
            pipeline.UseAuthorization();
        });
        web.MapGroup("/account").MapUserAccountEndpoints();
        try
        {
            await IdentityTestDatabase.MigrateAsync(web.Services);
            await web.StartAsync();
        }
        catch
        {
            await web.DisposeAsync();
            await database.DisposeAsync();
            throw;
        }

        return new LifetimeApp(web, database);
    }

    private sealed class LifetimeApp(WebApplication web, IdentityTestDatabase database) : IAsyncDisposable
    {
        /// <summary>A browser: its own cookie jar, sent back by hand, as the test server's client keeps none.</summary>
        public HttpClient CreateClient()
            => new(new CookieJar { InnerHandler = web.GetTestServer().CreateHandler() }) { BaseAddress = new Uri("http://localhost") };

        public async Task<Guid> SignUpAsync(string email)
        {
            await using var scope = web.Services.CreateAsyncScope();
            var user = new IdentityUser { UserName = email, Email = email };
            (await scope.ServiceProvider.GetRequiredService<UserPasswordService>().CreateWithPasswordAsync(user, Password)).IsSuccess.Should().BeTrue();
            return user.Id;
        }

        /// <summary>Signs in by cookie and returns the session cookie the response set.</summary>
        public async Task<string> SignInAsync(HttpClient client, bool isPersistent)
        {
            using var response = await client.PostAsJsonAsync("/account/sign-in?useCookies=true", new { identifier = Email, password = Password, isPersistent });
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
            return response.Headers.GetValues("Set-Cookie").Last();
        }

        public async Task<List<IdentitySession>> SessionsAsync(Guid userId)
        {
            await using var scope = web.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Sessions.AsNoTracking().Where(s => s.UserId == userId).ToListAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await web.DisposeAsync();
            await database.DisposeAsync();
        }
    }

    /// <summary>Keeps the last cookie each response set and sends it on every request, whatever its Secure flag.</summary>
    private sealed class CookieJar : DelegatingHandler
    {
        private string? _cookie;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_cookie is not null)
                request.Headers.Add("Cookie", _cookie);

            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
            {
                var pair = values.Last().Split(';')[0];
                _cookie = pair.EndsWith('=') ? null : pair;
            }

            return response;
        }
    }
}
