using Microsoft.AspNetCore.Http;

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff;

/// <summary>Matches API requests: a path under one of <see cref="IdentityBffOptions.EffectiveApiPathPrefixes"/>.</summary>
internal static class BffRequestMatcher
{
    /// <summary>Whether <paramref name="request"/> is an API call, answered with problems rather than redirects.</summary>
    /// <param name="request">The request.</param>
    /// <param name="options">The BFF options.</param>
    public static bool IsApi(HttpRequest request, IdentityBffOptions options)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);
        return options.EffectiveApiPathPrefixes.Any(prefix => request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether <paramref name="method"/> may change state, so a session's request with it needs the antiforgery header.</summary>
    /// <param name="method">The HTTP method.</param>
    public static bool IsUnsafe(string method)
        => !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method));
}
