using Microsoft.AspNetCore.Http;

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff;

/// <summary>Builds the RFC 7807 problems the BFF answers, with a stable <c>code</c>; the host's problem details service enriches them when registered.</summary>
internal static class BffProblemWriter
{
    /// <summary>The problem result for <paramref name="status"/>.</summary>
    /// <param name="status">The HTTP status.</param>
    /// <param name="code">A <see cref="BffErrorCodeConstants"/> value.</param>
    /// <param name="detail">What went wrong, for a person reading the response.</param>
    public static IResult Create(int status, string code, string detail)
        => Results.Problem(detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });

    /// <summary>Write the problem for <paramref name="status"/> to <paramref name="http"/>.</summary>
    /// <param name="http">The request.</param>
    /// <param name="status">The HTTP status.</param>
    /// <param name="code">A <see cref="BffErrorCodeConstants"/> value.</param>
    /// <param name="detail">What went wrong, for a person reading the response.</param>
    public static Task WriteAsync(HttpContext http, int status, string code, string detail)
    {
        ArgumentNullException.ThrowIfNull(http);
        return Create(status, code, detail).ExecuteAsync(http);
    }
}
