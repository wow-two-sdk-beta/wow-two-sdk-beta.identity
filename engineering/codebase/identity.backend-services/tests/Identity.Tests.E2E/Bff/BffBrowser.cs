using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using WoW.Two.Sdk.Beta.Be.Identity.Bff;
using Identity.Tests.E2E.Oidc;

namespace Identity.Tests.E2E.Bff;

/// <summary>
/// A browser over two in-memory hosts: the instance at <c>https://localhost</c> and the BFF at
/// <see cref="BffHost.Origin"/>. It keeps each host's cookies, returns redirects rather than following them, and walks the
/// code flow a person's browser walks.
/// </summary>
public sealed class BffBrowser : IDisposable
{
    /// <summary>The instance's origin.</summary>
    public const string IdentityOrigin = "https://localhost";

    private readonly HttpClient _http;

    /// <summary>Create a browser with no cookies.</summary>
    /// <param name="identity">The instance.</param>
    /// <param name="app">The BFF.</param>
    public BffBrowser(OidcHost identity, BffHost app)
    {
        var router = new HostRouter(new Dictionary<string, HttpMessageHandler>(StringComparer.OrdinalIgnoreCase)
        {
            ["localhost"] = identity.Host.Server.CreateHandler(),
            [BffHost.HostName] = app.CreateHandler(),
        });
        _http = new HttpClient(new CookieContainerHandler(Cookies) { InnerHandler = router }) { BaseAddress = new Uri(BffHost.Origin) };
    }

    /// <summary>Gets the cookies of both hosts.</summary>
    public CookieContainer Cookies { get; } = new();

    /// <summary>GET <paramref name="url"/>, relative to the BFF.</summary>
    public Task<HttpResponseMessage> GetAsync(string url) => _http.GetAsync(url);

    /// <summary>Send <paramref name="method"/> to <paramref name="url"/>, with the antiforgery header when <paramref name="csrfToken"/> is set.</summary>
    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? csrfToken = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (csrfToken is not null)
            request.Headers.Add(BffHeaderConstants.CsrfToken, csrfToken);
        return _http.SendAsync(request);
    }

    /// <summary>Sign up at the instance and sign in there with its session cookie; returns the person's id.</summary>
    public async Task<Guid> SignUpAtTheInstanceAsync(string email, string password)
    {
        using var registered = await _http.PostAsJsonAsync(IdentityOrigin + "/api/account/sign-up", new { email, password });
        registered.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await registered.Content.ReadAsStringAsync());
        using var signedIn = await _http.PostAsJsonAsync(IdentityOrigin + "/api/account/sign-in?useCookies=true", new { identifier = email, password });
        signedIn.StatusCode.Should().Be(HttpStatusCode.NoContent);
        return Guid.Parse(body.RootElement.GetProperty("data").GetProperty("userId").GetString()!);
    }

    /// <summary>
    /// Walk the code flow from the BFF's sign-in route: sign-in → the instance's authorize endpoint → the BFF's callback.
    /// Returns where the callback sends the browser, signed in.
    /// </summary>
    /// <param name="returnUrl">Where to land once signed in.</param>
    public async Task<string> SignInToTheAppAsync(string returnUrl = "/desk")
    {
        using var signIn = await _http.GetAsync("/api/identity/sign-in?returnUrl=" + Uri.EscapeDataString(returnUrl));
        signIn.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var authorize = signIn.Headers.Location!;
        authorize.GetLeftPart(UriPartial.Path).Should().Be(IdentityOrigin + "/connect/authorize");

        using var authorized = await _http.GetAsync(authorize);
        authorized.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var callback = authorized.Headers.Location!;
        callback.GetLeftPart(UriPartial.Path).Should().Be(BffHost.Origin + "/api/identity/callback", "a signed-in person comes straight back with a code");

        using var landed = await _http.GetAsync(callback);
        landed.StatusCode.Should().Be(HttpStatusCode.Redirect, await landed.Content.ReadAsStringAsync());
        return landed.Headers.Location!.OriginalString;
    }

    /// <summary>The <c>data</c> of the BFF's session route.</summary>
    public async Task<JsonElement> SessionAsync()
    {
        using var document = JsonDocument.Parse(await _http.GetStringAsync("/api/identity/session"));
        return document.RootElement.GetProperty("data").Clone();
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    /// <summary>Sends each request to the in-memory host its URL names.</summary>
    private sealed class HostRouter(IReadOnlyDictionary<string, HttpMessageHandler> hosts) : HttpMessageHandler
    {
        private readonly Dictionary<string, HttpMessageInvoker> _invokers = hosts.ToDictionary(
            pair => pair.Key, pair => new HttpMessageInvoker(pair.Value), StringComparer.OrdinalIgnoreCase);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _invokers.TryGetValue(request.RequestUri!.Host, out var invoker)
                ? invoker.SendAsync(request, cancellationToken)
                : throw new InvalidOperationException($"No in-memory host answers {request.RequestUri.Host}.");

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var invoker in _invokers.Values)
                    invoker.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
