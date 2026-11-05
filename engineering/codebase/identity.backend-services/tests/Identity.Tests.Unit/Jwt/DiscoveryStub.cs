using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Jwt;

/// <summary>Answers discovery as an OpenID Connect issuer does, from keys a test can rotate, and counts what was asked.</summary>
/// <param name="host">The authority the documents point at, such as <c>https://issuer.test</c>.</param>
internal sealed class DiscoveryStub(string host) : HttpMessageHandler
{
    private const string MetadataPath = "/.well-known/openid-configuration";

    private volatile RsaSecurityKey[] _keys = [];

    public ConcurrentQueue<Uri> Requests { get; } = new();

    public TimeSpan KeySetDelay { get; set; } = TimeSpan.Zero;

    /// <summary>Whether every request answers <c>503</c>.</summary>
    public bool IsDown { get; set; }

    /// <summary>The fetches of the discovery document.</summary>
    public int MetadataFetches => Requests.Count(uri => uri.AbsolutePath == MetadataPath);

    public void Publish(params RsaSecurityKey[] keys) => _keys = keys;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(request.RequestUri!);
        if (IsDown)
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        if (request.RequestUri!.AbsolutePath == MetadataPath)
        {
            return Json(new Dictionary<string, object>
            {
                ["issuer"] = host + "/",
                ["jwks_uri"] = host + "/jwks",
            });
        }

        await Task.Delay(KeySetDelay, cancellationToken);
        return Json(new Dictionary<string, object> { ["keys"] = _keys.Select(ToJsonWebKey).ToArray() });
    }

    private static Dictionary<string, string> ToJsonWebKey(RsaSecurityKey key)
    {
        var parameters = key.Rsa.ExportParameters(includePrivateParameters: false);
        return new Dictionary<string, string>
        {
            ["kty"] = "RSA",
            ["use"] = "sig",
            ["alg"] = SecurityAlgorithms.RsaSha256,
            ["kid"] = key.KeyId,
            ["n"] = Base64UrlEncoder.Encode(parameters.Modulus),
            ["e"] = Base64UrlEncoder.Encode(parameters.Exponent),
        };
    }

    private static HttpResponseMessage Json(Dictionary<string, object> body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
}
