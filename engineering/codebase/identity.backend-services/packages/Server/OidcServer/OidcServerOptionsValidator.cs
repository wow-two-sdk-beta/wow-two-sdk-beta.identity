using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration;

namespace WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

/// <summary>
/// Checks <see cref="OidcServerOptions"/> at boot: positive lifetimes, a local sign-in path, and clients that are
/// confidential with unique ids and exact absolute redirect URIs (HTTPS, or HTTP on a loopback host, no fragment); a
/// deployed boot also needs a pinned issuer.
/// </summary>
/// <param name="services">The root provider, to tell a local run from a deployed boot; null checks only the options.</param>
internal sealed class OidcServerOptionsValidator(IServiceProvider? services = null) : IValidateOptions<OidcServerOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, OidcServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var problems = new List<string>();
        foreach (var (setting, lifetime) in new[]
                 {
                     (nameof(options.AuthorizationCodeLifetime), options.AuthorizationCodeLifetime),
                     (nameof(options.AccessTokenLifetime), options.AccessTokenLifetime),
                     (nameof(options.IdentityTokenLifetime), options.IdentityTokenLifetime),
                     (nameof(options.RefreshTokenLifetime), options.RefreshTokenLifetime),
                 })
        {
            if (lifetime <= TimeSpan.Zero)
                problems.Add($"{OidcServerOptions.SectionName}:{setting} must be positive.");
        }

        if (string.IsNullOrWhiteSpace(options.SignInPath) || !options.SignInPath.StartsWith('/') || options.SignInPath.StartsWith("//", StringComparison.Ordinal))
            problems.Add($"{OidcServerOptions.SectionName}:SignInPath must be a local path such as /sign-in.");

        if (options.Issuer is { } issuer && (!issuer.IsAbsoluteUri || !IsAllowedScheme(issuer)))
            problems.Add($"{OidcServerOptions.SectionName}:Issuer must be an absolute HTTPS URI.");

        // Unpinned, the issuer follows each request's host, and the app and console sites answer on different hosts.
        if (options.Issuer is null && services is not null && !IsLocalRun(services))
            problems.Add($"{OidcServerOptions.SectionName}:Issuer is required on a deployed boot; only a local run may take the request's host.");

        foreach (var duplicate in options.Clients.GroupBy(c => c.ClientId, StringComparer.Ordinal).Where(g => g.Count() > 1))
            problems.Add($"Client '{duplicate.Key}' is registered {duplicate.Count()} times.");

        foreach (var client in options.Clients)
            problems.AddRange(CheckClient(client));

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }

    private static IEnumerable<string> CheckClient(OidcClientOptions client)
    {
        var label = string.IsNullOrWhiteSpace(client.ClientId) ? "A client" : $"Client '{client.ClientId}'";
        if (string.IsNullOrWhiteSpace(client.ClientId))
            yield return "A client has no ClientId.";
        if (string.IsNullOrWhiteSpace(client.ClientSecret))
            yield return $"{label} has no ClientSecret; every client is confidential.";
        if (client.RedirectUris.Count == 0)
            yield return $"{label} has no RedirectUris.";
        if (client.Audience is { } audience && string.IsNullOrWhiteSpace(audience))
            yield return $"{label} has a blank Audience.";
        if (client.PermissionNamespace is { } ns && (string.IsNullOrWhiteSpace(ns) || ns.Contains(':', StringComparison.Ordinal)))
            yield return $"{label} has a PermissionNamespace that is blank or holds ':'.";

        foreach (var uri in client.RedirectUris.Concat(client.PostLogoutRedirectUris))
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || !IsAllowedScheme(parsed) || !string.IsNullOrEmpty(parsed.Fragment))
                yield return $"{label} lists '{uri}', which is not an absolute HTTPS URI (HTTP only on a loopback host) without a fragment.";
        }
    }

    private static bool IsLocalRun(IServiceProvider services)
        => services.GetService<IHostEnvironment>() is { } environment
            && services.GetService<IConfiguration>() is { } configuration
            && environment.IsLocalRun(configuration);

    private static bool IsAllowedScheme(Uri uri)
        => uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
}
