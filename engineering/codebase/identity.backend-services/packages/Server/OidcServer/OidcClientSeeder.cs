using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

/// <summary>
/// Seeds the client registry from <see cref="OidcServerOptions.Clients"/>, idempotently: a new client is created, a
/// changed one updated, an unchanged one left alone, and a client this seeder created that left configuration is
/// deleted with its grants and tokens. Clients registered any other way are never touched.
/// </summary>
/// <param name="applications">The OpenIddict application manager.</param>
/// <param name="options">The server options carrying the clients.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class OidcClientSeeder(IOpenIddictApplicationManager applications, OidcServerOptions options, ILogger<OidcClientSeeder> logger)
{
    /// <summary>Bring the registry in line with configuration.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        foreach (var client in options.Clients)
        {
            var desired = CreateDescriptor(client, options.Resources);
            if (await applications.FindByClientIdAsync(client.ClientId, cancellationToken) is not { } existing)
            {
                await applications.CreateAsync(desired, cancellationToken);
                LogSeeded(logger, client.ClientId, "created");
                continue;
            }

            var current = new OpenIddictApplicationDescriptor();
            await applications.PopulateAsync(current, existing, cancellationToken);

            // A matching secret keeps its stored hash; hashing it anew would rewrite the row at every boot.
            var secretMatches = client.ClientSecret is { } secret
                ? await applications.ValidateClientSecretAsync(existing, secret, cancellationToken)
                : string.IsNullOrEmpty(current.ClientSecret);
            if (secretMatches && IsSameRegistration(current, desired))
                continue;

            desired.ClientSecret = secretMatches ? current.ClientSecret : client.ClientSecret;
            await applications.UpdateAsync(existing, desired, cancellationToken);
            LogSeeded(logger, client.ClientId, "updated");
        }

        var configured = options.Clients.Select(client => client.ClientId).ToHashSet(StringComparer.Ordinal);
        var removed = new List<object>();
        await foreach (var application in applications.ListAsync(count: null, offset: null, cancellationToken))
        {
            var properties = await applications.GetPropertiesAsync(application, cancellationToken);
            if (properties.TryGetValue(OidcClientProfile.SourceProperty, out var source)
                && source.ValueKind == JsonValueKind.String
                && source.GetString() == OidcClientProfile.ConfigurationSource
                && await applications.GetClientIdAsync(application, cancellationToken) is { } clientId
                && !configured.Contains(clientId))
                removed.Add(application);
        }

        foreach (var application in removed)
        {
            var clientId = await applications.GetClientIdAsync(application, cancellationToken);
            await applications.DeleteAsync(application, cancellationToken);
            LogSeeded(logger, clientId ?? "?", "deleted");
        }
    }

    /// <summary>
    /// The registration of <paramref name="client"/>: confidential and first-party, with the code flow, PKCE and refresh
    /// tokens while it signs people in, and each service grant it names with its resources and scopes.
    /// </summary>
    /// <param name="client">The configured client.</param>
    /// <param name="resources">The resources its names refer to.</param>
    internal static OpenIddictApplicationDescriptor CreateDescriptor(OidcClientOptions client, IReadOnlyList<OidcResourceOptions>? resources = null)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ApplicationType = ApplicationTypes.Web,
            ClientId = client.ClientId,
            ClientSecret = client.ClientSecret,
            JsonWebKeySet = client.JsonWebKeySet is { } keys ? Microsoft.IdentityModel.Tokens.JsonWebKeySet.Create(keys) : null,
            ClientType = ClientTypes.Confidential,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = client.DisplayName ?? client.ClientId,
        };

        descriptor.Permissions.Add(Permissions.Endpoints.Token);
        if (client.Grants.Count == 0 || client.Grants.Contains(OidcClientGrantConstants.AuthorizationCode))
        {
            descriptor.Permissions.UnionWith(
            [
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.EndSession,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Email,
                Permissions.Scopes.Phone,
                Permissions.Scopes.Profile,
            ]);
            descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
        }

        descriptor.RedirectUris.UnionWith(client.RedirectUris.Select(uri => new Uri(uri, UriKind.Absolute)));
        descriptor.PostLogoutRedirectUris.UnionWith(client.PostSignOutRedirectUris.Select(uri => new Uri(uri, UriKind.Absolute)));
        descriptor.Properties[OidcClientProfile.AudienceProperty] = JsonSerializer.SerializeToElement(client.Audience ?? client.ClientId);
        descriptor.Properties[OidcClientProfile.SourceProperty] = JsonSerializer.SerializeToElement(OidcClientProfile.ConfigurationSource);
        if (client.PermissionNamespace is { } permissionNamespace)
            descriptor.Properties[OidcClientProfile.PermissionNamespaceProperty] = JsonSerializer.SerializeToElement(permissionNamespace);

        if (client.CanIntrospect)
            descriptor.Permissions.Add(Permissions.Endpoints.Introspection);

        AddServiceAccess(descriptor, client, resources ?? []);
        return descriptor;
    }

    /// <summary>
    /// Grant the client's service tokens: OpenIddict checks the grant, resource and scope permissions, and the token
    /// endpoint reads the <see cref="OidcClientServiceAccess"/> property to tell which grant reaches which resource.
    /// </summary>
    private static void AddServiceAccess(OpenIddictApplicationDescriptor descriptor, OidcClientOptions client, IReadOnlyList<OidcResourceOptions> resources)
    {
        var canCallAsItself = client.Grants.Contains(OidcClientGrantConstants.ClientCredentials);
        var canCallForPerson = client.Grants.Contains(OidcClientGrantConstants.TokenExchange);
        if (!canCallAsItself && !canCallForPerson)
            return;

        var credentialResources = canCallAsItself ? UrisOf(client.Resources, resources) : [];
        var exchangeResources = canCallForPerson ? UrisOf(client.ExchangeResources, resources) : [];
        if (canCallAsItself)
            descriptor.Permissions.Add(Permissions.GrantTypes.ClientCredentials);
        if (canCallForPerson)
            descriptor.Permissions.Add(Permissions.GrantTypes.TokenExchange);

        descriptor.Permissions.UnionWith(credentialResources.Concat(exchangeResources).Select(uri => Permissions.Prefixes.Resource + uri));
        descriptor.Permissions.UnionWith(client.Scopes.Select(scope => Permissions.Prefixes.Scope + scope));
        descriptor.Properties[OidcClientProfile.ServiceAccessProperty] = JsonSerializer.SerializeToElement(new OidcClientServiceAccess
        {
            CredentialResources = credentialResources,
            ExchangeResources = exchangeResources,
            Scopes = client.Scopes,
            OrganizationId = canCallAsItself ? client.OrganizationId : null,
        });
    }

    private static string[] UrisOf(IEnumerable<string> names, IReadOnlyList<OidcResourceOptions> resources)
        => names.Select(name => resources.First(resource => resource.Name == name).Uri).ToArray();

    /// <summary>Whether two registrations agree on everything but the secret.</summary>
    internal static bool IsSameRegistration(OpenIddictApplicationDescriptor current, OpenIddictApplicationDescriptor desired)
        => current.ApplicationType == desired.ApplicationType
            && current.ClientType == desired.ClientType
            && current.ConsentType == desired.ConsentType
            && current.DisplayName == desired.DisplayName
            && KeysOf(current).SequenceEqual(KeysOf(desired))
            && current.Permissions.SetEquals(desired.Permissions)
            && current.Requirements.SetEquals(desired.Requirements)
            && current.RedirectUris.SetEquals(desired.RedirectUris)
            && current.PostLogoutRedirectUris.SetEquals(desired.PostLogoutRedirectUris)
            && current.Properties.Count == desired.Properties.Count
            && desired.Properties.All(pair => current.Properties.TryGetValue(pair.Key, out var value) && value.GetRawText() == pair.Value.GetRawText());

    /// <summary>What identifies each public key of a registration, in a stable order.</summary>
    private static IEnumerable<string> KeysOf(OpenIddictApplicationDescriptor descriptor)
        => (descriptor.JsonWebKeySet?.Keys ?? []).Select(key => $"{key.Kty}|{key.Kid}|{key.N}|{key.E}|{key.Crv}|{key.X}|{key.Y}").Order(StringComparer.Ordinal);

    [LoggerMessage(EventId = 7111, Level = LogLevel.Information, Message = "OIDC client {ClientId} {Change} from configuration")]
    private static partial void LogSeeded(ILogger logger, string clientId, string change);
}
