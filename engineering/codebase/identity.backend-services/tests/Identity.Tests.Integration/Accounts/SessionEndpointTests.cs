using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Beta.Be.Identity.Cookies;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Lockout;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SecurityStamps;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Sessions;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignIn;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignInLog;
using WoW.Two.Sdk.Beta.Be.Meta;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Accounts;

/// <summary>
/// Sessions through the account API's cookie: each cookie sign-in starts a session whose <c>sid</c> the cookie carries,
/// the person lists and revokes them, and a revoked session's cookie stops working at once; the sign-in log ties each
/// successful attempt to its session.
/// </summary>
public sealed class SessionEndpointTests
{
    private const string Password = "correct horse battery";
    private const string Email = "erin@example.test";

    [Fact]
    public async Task CookieSessions_ShouldBeListedAndRevocable_OneOrAll()
    {
        await using var app = await StartAsync();
        await app.RegisterAsync(Email);
        using var laptop = app.CreateClient("Laptop/1.0");
        using var phone = app.CreateClient("Phone/2.0");
        await app.SignInAsync(laptop, Email);
        await app.SignInAsync(phone, Email);

        var listed = await ListAsync(laptop);
        listed.Should().HaveCount(2);
        var phoneSession = listed.Single(session => session.GetProperty("userAgent").GetString() == "Phone/2.0");
        var laptopSession = listed.Single(session => session.GetProperty("current").GetBoolean());
        laptopSession.GetProperty("userAgent").GetString().Should().Be("Laptop/1.0");
        laptopSession.GetProperty("ipMasked").GetString().Should().Be("203.0.113.0");
        laptopSession.GetProperty("authenticationMethods").EnumerateArray().Select(v => v.GetString()).Should().Equal("pwd");

        (await laptop.DeleteAsync($"/account/manage/sessions/{phoneSession.GetProperty("id").GetGuid()}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await phone.GetAsync("/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the revoked session's cookie ends at once");
        (await laptop.GetAsync("/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await laptop.DeleteAsync($"/account/manage/sessions/{phoneSession.GetProperty("id").GetGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await app.SignInAsync(phone, Email);
        (await laptop.DeleteAsync("/account/manage/sessions?keepCurrent=true")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await phone.GetAsync("/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ListAsync(laptop)).Should().ContainSingle().Which.GetProperty("current").GetBoolean().Should().BeTrue();

        (await laptop.DeleteAsync("/account/manage/sessions")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await laptop.GetAsync("/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_ShouldEndTheSession_AndAStampRotationEveryOther()
    {
        await using var app = await StartAsync();
        var userId = await app.RegisterAsync(Email);
        using var first = app.CreateClient("First/1.0");
        using var second = app.CreateClient("Second/1.0");
        await app.SignInAsync(first, Email);
        await app.SignInAsync(second, Email);

        (await first.PostAsJsonAsync("/account/logout", new { })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var rows = await app.SessionsAsync(userId);
        rows.Should().ContainSingle(row => row.RevokeReason == SessionRevokeReasonConstants.SignOut && row.UserAgent == "First/1.0");

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<UserAccountService>();
            await accounts.RotateSecurityStampAsync((await accounts.FindByIdAsync(userId))!);
        }

        (await second.GetAsync("/account/manage/info")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await app.SessionsAsync(userId)).Should().OnlyContain(row => row.RevokedAt != null);
    }

    [Fact]
    public async Task SignInLog_ShouldTieEachSuccessToItsSession_AndKeepTheRequestMasked()
    {
        await using var app = await StartAsync();
        var userId = await app.RegisterAsync(Email);
        using var browser = app.CreateClient("Browser/3.0");

        using var wrong = await browser.PostAsJsonAsync("/account/login?useCookies=true&clientId=wheelhouse", new { login = Email, password = "wrong" });
        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await app.SignInAsync(browser, Email, "?clientId=wheelhouse");

        await using var scope = app.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().SignInLog.AsNoTracking().OrderBy(row => row.Id).ToListAsync();
        var session = (await app.SessionsAsync(userId)).Single();
        rows.Select(row => (row.Result, row.Reason, row.SessionId)).Should().Equal(
            (SignInResultConstants.Failed, SignInReasonConstants.WrongPassword, null),
            (SignInResultConstants.Succeeded, null, session.Id));
        rows.Should().OnlyContain(row =>
            row.UserId == userId && row.ClientId == "wheelhouse" && row.IpMasked == "203.0.113.0" && row.UserAgent == "Browser/3.0" && row.SubjectMasked == "e***@example.test");
    }

    private static async Task<IReadOnlyList<JsonElement>> ListAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/account/manage/sessions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return [.. document.RootElement.GetProperty("data").EnumerateArray().Select(element => element.Clone())];
    }

    private static async Task<SessionApp> StartAsync()
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
            .AddLockout()
            .AddSecurityStampValidation(o => o.ValidationInterval = TimeSpan.Zero)
            .AddSessions()
            .AddSignInLog()
            .AddSignIn()
            .AddAccountEndpoints(o => o.Enabled = true);
        builder.Services.AddCookieAuthentication(o => o.Mode = AuthChallengeMode.Api);
        builder.Services.AddAuthorization();

        var web = builder.Build();
        web.UseApiDefaults(pipeline =>
        {
            // A documentation address, as a trusted proxy would forward it.
            pipeline.Use((context, next) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.77");
                return next(context);
            });
            pipeline.UseAuthentication();
            pipeline.UseAuthorization();
        });
        web.MapGroup("/account").MapUserAccountEndpoints();
        await IdentityTestDatabase.MigrateAsync(web.Services);
        await web.StartAsync();
        return new SessionApp(web, database);
    }

    private sealed class SessionApp(WebApplication web, IdentityTestDatabase database) : IAsyncDisposable
    {
        public IServiceProvider Services => web.Services;

        /// <summary>A browser: its own cookie jar, sent back by hand, as the test server's client keeps none.</summary>
        public HttpClient CreateClient(string userAgent)
        {
            var client = new HttpClient(new CookieJar { InnerHandler = web.GetTestServer().CreateHandler() }) { BaseAddress = new Uri("http://localhost") };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            return client;
        }

        public async Task<Guid> RegisterAsync(string email)
        {
            await using var scope = web.Services.CreateAsyncScope();
            var user = new IdentityUser { UserName = email, Email = email };
            (await scope.ServiceProvider.GetRequiredService<UserPasswordService>().CreateWithPasswordAsync(user, Password)).Succeeded.Should().BeTrue();
            return user.Id;
        }

        public async Task SignInAsync(HttpClient client, string login, string query = "")
        {
            using var response = await client.PostAsJsonAsync($"/account/login?useCookies=true{query.Replace('?', '&')}", new { login, password = Password });
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
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
