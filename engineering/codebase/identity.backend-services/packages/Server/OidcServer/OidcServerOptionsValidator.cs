using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration;

namespace WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

/// <summary>
/// Checks <see cref="OidcServerOptions"/> at boot: positive lifetimes, a local sign-in path, clients that are confidential
/// with unique ids and exact absolute redirect URIs (HTTPS, or HTTP on a loopback host, no fragment), resources with a
/// canonical URI and <c>{service}.{action}</c> scopes, and service grants that name only registered resources and the
/// scopes those understand; a deployed boot also needs a pinned issuer.
/// </summary>
/// <param name="services">The root provider, to tell a local run from a deployed boot; null checks only the options.</param>
internal sealed partial class OidcServerOptionsValidator(IServiceProvider? services = null) : IValidateOptions<OidcServerOptions>
{
    private static readonly TimeSpan MaxServiceTokenLifetime = TimeSpan.FromMinutes(10);

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

        if (options.ServiceTokenLifetime > MaxServiceTokenLifetime)
            problems.Add($"{OidcServerOptions.SectionName}:ServiceTokenLifetime must be at most {MaxServiceTokenLifetime.TotalMinutes:0} minutes; a service token has no way to be revoked.");
        if (options.ServiceTokenLifetime <= TimeSpan.Zero)
            problems.Add($"{OidcServerOptions.SectionName}:ServiceTokenLifetime must be positive.");

        if (string.IsNullOrWhiteSpace(options.SignInPath) || !options.SignInPath.StartsWith('/') || options.SignInPath.StartsWith("//", StringComparison.Ordinal))
            problems.Add($"{OidcServerOptions.SectionName}:SignInPath must be a local path such as /sign-in.");

        if (options.Issuer is { } issuer && (!issuer.IsAbsoluteUri || !IsAllowedScheme(issuer)))
            problems.Add($"{OidcServerOptions.SectionName}:Issuer must be an absolute HTTPS URI.");

        // Unpinned, the issuer follows each request's host, and the app and console sites answer on different hosts.
        if (options.Issuer is null && services is not null && !IsLocalRun(services))
            problems.Add($"{OidcServerOptions.SectionName}:Issuer is required on a deployed boot; only a local run may take the request's host.");

        foreach (var duplicate in options.Clients.GroupBy(c => c.ClientId, StringComparer.Ordinal).Where(g => g.Count() > 1))
            problems.Add($"Client '{duplicate.Key}' is registered {duplicate.Count()} times.");

        foreach (var duplicate in options.Resources.GroupBy(r => r.Name, StringComparer.Ordinal).Where(g => g.Count() > 1))
            problems.Add($"Resource '{duplicate.Key}' is registered {duplicate.Count()} times.");
        foreach (var duplicate in options.Resources.GroupBy(r => r.Uri, StringComparer.Ordinal).Where(g => g.Count() > 1))
            problems.Add($"Resource URI '{duplicate.Key}' is registered {duplicate.Count()} times; each audience names one resource.");

        foreach (var resource in options.Resources)
            problems.AddRange(CheckResource(resource));

        foreach (var client in options.Clients)
            problems.AddRange(CheckClient(client, options.Resources));

        problems.AddRange(CheckSignalUrls(options));

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }

    /// <summary>A signal URL is absolute with the scheme <c>Signals:RequireHttps</c> allows, and needs a pinned issuer, since the signature names it.</summary>
    private static IEnumerable<string> CheckSignalUrls(OidcServerOptions options)
    {
        var urls = options.Clients.Select(client => client.SignalUrl).Concat(options.Resources.Select(resource => resource.SignalUrl)).OfType<string>().ToList();
        if (urls.Count > 0 && options.Issuer is null)
            yield return $"{OidcServerOptions.SectionName}:Issuer is required once a client or resource names a SignalUrl; the signature names it.";

        foreach (var url in urls)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
                || (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)
                || (options.Signals.RequireHttps && !IsAllowedScheme(parsed)))
                yield return $"The SignalUrl '{url}' is not an absolute HTTPS URI (HTTP on a loopback host, or anywhere with Signals:RequireHttps off).";
        }
    }

    private static IEnumerable<string> CheckResource(OidcResourceOptions resource)
    {
        var label = string.IsNullOrWhiteSpace(resource.Name) ? "A resource" : $"Resource '{resource.Name}'";
        if (string.IsNullOrWhiteSpace(resource.Name))
            yield return "A resource has no Name.";
        if (!Uri.TryCreate(resource.Uri, UriKind.Absolute, out var uri) || !IsAllowedScheme(uri) || !string.IsNullOrEmpty(uri.Fragment))
            yield return $"{label} has the URI '{resource.Uri}', which is not an absolute HTTPS URI (HTTP only on a loopback host) without a fragment.";
        else if (!string.Equals(uri.AbsoluteUri, resource.Uri, StringComparison.Ordinal))
            yield return $"{label} must write its URI as '{uri.AbsoluteUri}'; callers send it and tokens carry it exactly so.";
        if (resource.Scopes.Count == 0)
            yield return $"{label} has no Scopes.";
        if (resource.PermissionNamespace is { } ns && (string.IsNullOrWhiteSpace(ns) || ns.Contains(':', StringComparison.Ordinal)))
            yield return $"{label} has a PermissionNamespace that is blank or holds ':'.";

        foreach (var scope in resource.Scopes.Where(scope => !ScopeName().IsMatch(scope)))
            yield return $"{label} lists the scope '{scope}', which is not {{service}}.{{action}} in lowercase.";
    }

    private static IEnumerable<string> CheckClient(OidcClientOptions client, IReadOnlyList<OidcResourceOptions> resources)
    {
        var label = string.IsNullOrWhiteSpace(client.ClientId) ? "A client" : $"Client '{client.ClientId}'";
        if (string.IsNullOrWhiteSpace(client.ClientId))
            yield return "A client has no ClientId.";
        if (string.IsNullOrWhiteSpace(client.ClientSecret) && string.IsNullOrWhiteSpace(client.JsonWebKeySet))
            yield return $"{label} has no ClientSecret or JsonWebKeySet; every client is confidential.";
        if (!string.IsNullOrWhiteSpace(client.ClientSecret) && !string.IsNullOrWhiteSpace(client.JsonWebKeySet))
            yield return $"{label} has both a ClientSecret and a JsonWebKeySet; it authenticates one way.";
        if (!string.IsNullOrWhiteSpace(client.JsonWebKeySet) && CheckKeySet(client.JsonWebKeySet) is { } keyProblem)
            yield return $"{label} has a JsonWebKeySet that {keyProblem}.";

        var signsPeopleIn = client.Grants.Count == 0 || client.Grants.Contains(OidcClientGrantConstants.AuthorizationCode);
        if (signsPeopleIn && client.RedirectUris.Count == 0)
            yield return $"{label} has no RedirectUris.";
        if (!signsPeopleIn && client.RedirectUris.Concat(client.PostSignOutRedirectUris).Any())
            yield return $"{label} lists redirect URIs without the {OidcClientGrantConstants.AuthorizationCode} grant.";

        foreach (var problem in CheckServiceAccess(client, label, resources))
            yield return problem;

        if (client.Audience is { } audience && string.IsNullOrWhiteSpace(audience))
            yield return $"{label} has a blank Audience.";
        if (client.PermissionNamespace is { } ns && (string.IsNullOrWhiteSpace(ns) || ns.Contains(':', StringComparison.Ordinal)))
            yield return $"{label} has a PermissionNamespace that is blank or holds ':'.";

        foreach (var uri in client.RedirectUris.Concat(client.PostSignOutRedirectUris))
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || !IsAllowedScheme(parsed) || !string.IsNullOrEmpty(parsed.Fragment))
                yield return $"{label} lists '{uri}', which is not an absolute HTTPS URI (HTTP only on a loopback host) without a fragment.";
        }
    }

    /// <summary>What is wrong with a client's key set: it must parse and hold only public RSA or EC signing keys.</summary>
    private static string? CheckKeySet(string json)
    {
        JsonWebKeySet set;
        try
        {
            set = JsonWebKeySet.Create(json);
        }
        catch (ArgumentException)
        {
            return "does not parse";
        }

        if (set.Keys.Count == 0)
            return "holds no key";
        if (set.Keys.Any(key => key.HasPrivateKey))
            return "holds a private key; only the app keeps that";

        return set.Keys.All(key => key.Kty is JsonWebAlgorithmsKeyTypes.RSA or JsonWebAlgorithmsKeyTypes.EllipticCurve && key.Use is JsonWebKeyUseNames.Sig or null)
            ? null
            : "holds a key that is not an RSA or EC signing key";
    }

    /// <summary>Each service grant needs its resources, each listed resource and scope its grant, and each name a registration.</summary>
    private static IEnumerable<string> CheckServiceAccess(OidcClientOptions client, string label, IReadOnlyList<OidcResourceOptions> resources)
    {
        foreach (var grant in client.Grants.Where(grant => grant is not (OidcClientGrantConstants.AuthorizationCode or OidcClientGrantConstants.ClientCredentials or OidcClientGrantConstants.TokenExchange)))
            yield return $"{label} lists the unknown grant '{grant}'.";

        var callsAsItself = client.Grants.Contains(OidcClientGrantConstants.ClientCredentials);
        var callsForPerson = client.Grants.Contains(OidcClientGrantConstants.TokenExchange);
        if (callsAsItself != client.Resources.Count > 0)
            yield return $"{label} needs both the {OidcClientGrantConstants.ClientCredentials} grant and Resources, or neither.";
        if (callsForPerson != client.ExchangeResources.Count > 0)
            yield return $"{label} needs both the {OidcClientGrantConstants.TokenExchange} grant and ExchangeResources, or neither.";
        if ((callsAsItself || callsForPerson) != client.Scopes.Count > 0)
            yield return $"{label} needs Scopes with a service grant, and none without one.";
        if (client.OrganizationId is not null && !callsAsItself)
            yield return $"{label} is bound to an organization without the {OidcClientGrantConstants.ClientCredentials} grant.";

        var named = client.Resources.Concat(client.ExchangeResources).Distinct(StringComparer.Ordinal).ToList();
        foreach (var name in named.Where(name => resources.All(resource => resource.Name != name)))
            yield return $"{label} names the resource '{name}', which is not registered.";

        var understood = resources.Where(resource => named.Contains(resource.Name)).SelectMany(resource => resource.Scopes).ToHashSet(StringComparer.Ordinal);
        foreach (var scope in client.Scopes.Where(scope => !understood.Contains(scope)))
            yield return $"{label} lists the scope '{scope}', which none of its resources understands.";
    }

    private static bool IsLocalRun(IServiceProvider services)
        => services.GetService<IHostEnvironment>() is { } environment
            && services.GetService<IConfiguration>() is { } configuration
            && environment.IsLocalRun(configuration);

    private static bool IsAllowedScheme(Uri uri)
        => uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);

    [GeneratedRegex("^[a-z][a-z0-9-]*\\.[a-z][a-z0-9-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ScopeName();
}
