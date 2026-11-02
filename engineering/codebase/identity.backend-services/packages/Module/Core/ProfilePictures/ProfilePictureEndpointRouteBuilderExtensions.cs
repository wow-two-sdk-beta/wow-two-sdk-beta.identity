using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.ProfilePictures;

/// <summary>Maps the public profile picture route onto the account API.</summary>
public static class ProfilePictureEndpointRouteBuilderExtensions
{
    /// <summary>The route under the account API's group: <c>pictures/{userId}</c>.</summary>
    public const string Pattern = "/pictures/{userId:guid}";

    /// <summary>
    /// Maps <c>GET pictures/{userId}</c>: the person's kept WebP, anonymous so apps show it in an <c>img</c>. With
    /// <c>?v={version}</c> naming the current version it is cached for a year as immutable, the link the <c>picture</c>
    /// claim carries; otherwise it revalidates by <c>ETag</c>. <c>404</c> without a picture.
    /// </summary>
    /// <param name="endpoints">The account API's route group.</param>
    public static IEndpointRouteBuilder MapProfilePictureEndpoint(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGet(Pattern, PictureAsync).AllowAnonymous();
        return endpoints;
    }

    private static async Task<IResult> PictureAsync(Guid userId, string? v, HttpContext http, CancellationToken cancellationToken)
    {
        var picture = await http.RequestServices.GetRequiredService<IProfilePictureStore>().FindAsync(userId, cancellationToken);
        if (picture is null)
            return Results.NotFound();

        var tag = new EntityTagHeaderValue($"\"{picture.Version}\"");
        var headers = http.Response.GetTypedHeaders();
        headers.ETag = tag;
        headers.CacheControl = string.Equals(v, picture.Version, StringComparison.Ordinal)
            ? new CacheControlHeaderValue { Public = true, MaxAge = TimeSpan.FromDays(365), Extensions = { new NameValueHeaderValue("immutable") } }
            : new CacheControlHeaderValue { Public = true, NoCache = true };
        http.Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        if (http.Request.GetTypedHeaders().IfNoneMatch.Any(candidate => candidate.Compare(tag, useStrongComparison: false)))
            return Results.StatusCode(StatusCodes.Status304NotModified);

        return Results.File(picture.Content, picture.ContentType);
    }
}
