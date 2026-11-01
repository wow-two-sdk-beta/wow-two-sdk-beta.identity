using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInDeclaration;

/// <summary>Maps the public sign-in declaration onto the account API.</summary>
public static class SignInDeclarationEndpointRouteBuilderExtensions
{
    /// <summary>The route under the account API's group.</summary>
    public const string Pattern = "/sign-in-options";

    /// <summary>How long a browser or proxy keeps an answer before it revalidates with the <c>ETag</c>.</summary>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Maps <c>GET sign-in-options</c> under <paramref name="endpoints"/>, the account API's group (<c>/api/account</c> in the
    /// service): anonymous, it answers <see cref="SignInOptionsDto"/> in the success envelope.
    /// </summary>
    /// <remarks>
    ///   - <c>?clientId=</c> names the app a sign-in serves: its own theme and background replace the instance's; an
    ///     unknown client answers exactly what no client does
    ///   - <c>?locale=</c> (<c>uz</c>, <c>ru</c>, <c>en</c>) picks the messages' locale, else <c>Accept-Language</c>, else <c>en</c>
    ///   - the answer carries a strong <c>ETag</c> over its body, <c>Cache-Control: public, max-age=60</c> and
    ///     <c>Vary: Accept-Language</c>; a matching <c>If-None-Match</c> answers <c>304</c>
    /// </remarks>
    /// <param name="endpoints">The account API's route group.</param>
    public static IEndpointRouteBuilder MapSignInOptionsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGet(Pattern, Get).AllowAnonymous();
        return endpoints;
    }

    private static IResult Get(string? clientId, string? locale, HttpContext http)
    {
        var services = http.RequestServices;
        var request = http.Request.GetTypedHeaders();
        var resolved = SignInLocaleResolver.Resolve(locale, request.AcceptLanguage);
        var declaration = services.GetRequiredService<SignInDeclarationService>().Build(clientId, resolved);
        var json = services.GetService<IOptions<HttpJsonOptions>>()?.Value.SerializerOptions ?? JsonSerializerOptions.Web;
        var body = JsonSerializer.SerializeToUtf8Bytes(ApiResponse<SignInOptionsDto>.Ok(declaration), json);
        var tag = new EntityTagHeaderValue($"\"{Convert.ToHexStringLower(SHA256.HashData(body), 0, 16)}\"");

        var response = http.Response.GetTypedHeaders();
        response.ETag = tag;
        response.CacheControl = new CacheControlHeaderValue { Public = true, MaxAge = CacheLifetime };
        http.Response.Headers.Vary = HeaderNames.AcceptLanguage;
        if (request.IfNoneMatch.Any(candidate => candidate.Equals(EntityTagHeaderValue.Any) || candidate.Compare(tag, useStrongComparison: false)))
            return Results.StatusCode(StatusCodes.Status304NotModified);

        return Results.Bytes(body, "application/json; charset=utf-8");
    }
}
