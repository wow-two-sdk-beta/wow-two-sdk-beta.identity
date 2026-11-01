using Microsoft.Extensions.Options;

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff;

/// <summary>Validates <see cref="IdentityBffOptions"/> at boot, so a missing instance or client fails the host rather than the first sign-in.</summary>
internal sealed class IdentityBffOptionsValidator : IValidateOptions<IdentityBffOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, IdentityBffOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        var section = IdentityBffOptions.SectionName;

        if (!Uri.TryCreate(options.Authority, UriKind.Absolute, out var authority))
            failures.Add($"{section}:{nameof(IdentityBffOptions.Authority)} must be the instance's absolute issuer URL.");
        else if (options.IsHttpsMetadataRequired && authority.Scheme != Uri.UriSchemeHttps)
            failures.Add($"{section}:{nameof(IdentityBffOptions.Authority)} must use HTTPS while {nameof(IdentityBffOptions.IsHttpsMetadataRequired)} is on.");

        if (string.IsNullOrWhiteSpace(options.ClientId))
            failures.Add($"{section}:{nameof(IdentityBffOptions.ClientId)} is required.");

        if (!options.EffectiveScopes.Contains("openid", StringComparer.Ordinal))
            failures.Add($"{section}:{nameof(IdentityBffOptions.Scopes)} must hold openid.");
        if (options.EffectiveScopes.Any(string.IsNullOrWhiteSpace))
            failures.Add($"{section}:{nameof(IdentityBffOptions.Scopes)} must not hold an empty scope.");

        if (!IsPrefix(options.RoutePrefix))
            failures.Add($"{section}:{nameof(IdentityBffOptions.RoutePrefix)} must be a path such as /auth: a leading slash, no trailing one.");
        if (options.EffectiveApiPathPrefixes.Any(prefix => !IsPrefix(prefix)))
            failures.Add($"{section}:{nameof(IdentityBffOptions.ApiPathPrefixes)} must hold paths such as /api: a leading slash, no trailing one.");

        if (!BffReturnUrl.IsLocal(options.PostSignOutRedirectPath))
            failures.Add($"{section}:{nameof(IdentityBffOptions.PostSignOutRedirectPath)} must be a local path such as /.");

        if (options.SessionLifetime <= TimeSpan.Zero)
            failures.Add($"{section}:{nameof(IdentityBffOptions.SessionLifetime)} must be positive.");
        if (options.AccessTokenRefreshMargin < TimeSpan.Zero)
            failures.Add($"{section}:{nameof(IdentityBffOptions.AccessTokenRefreshMargin)} must not be negative.");

        if (string.IsNullOrWhiteSpace(options.CookieName))
            failures.Add($"{section}:{nameof(IdentityBffOptions.CookieName)} is required.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>Whether <paramref name="path"/> is a non-root path with a leading and no trailing slash.</summary>
    private static bool IsPrefix(string? path)
        => !string.IsNullOrEmpty(path) && path.Length > 1 && path[0] == '/' && path[^1] != '/' && BffReturnUrl.IsLocal(path);
}
