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
            var desired = CreateDescriptor(client);
            if (await applications.FindByClientIdAsync(client.ClientId, cancellationToken) is not { } existing)
            {
                await applications.CreateAsync(desired, cancellationToken);
                LogSeeded(logger, client.ClientId, "created");
                continue;
            }

            var current = new OpenIddictApplicationDescriptor();
            await applications.PopulateAsync(current, existing, cancellationToken);

            // A matching secret keeps its stored hash; hashing it anew would rewrite the row at every boot.
            var secretMatches = await applications.ValidateClientSecretAsync(existing, client.ClientSecret!, cancellationToken);
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

    /// <summary>The registration of <paramref name="client"/>: confidential, first-party, code flow with PKCE and refresh tokens.</summary>
    /// <param name="client">The configured client.</param>
    internal static OpenIddictApplicationDescriptor CreateDescriptor(OidcClientOptions client)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ApplicationType = ApplicationTypes.Web,
            ClientId = client.ClientId,
            ClientSecret = client.ClientSecret,
            ClientType = ClientTypes.Confidential,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = client.DisplayName ?? client.ClientId,
        };

        descriptor.Permissions.UnionWith(
        [
            Permissions.Endpoints.Authorization,
            Permissions.Endpoints.Token,
            Permissions.Endpoints.EndSession,
            Permissions.GrantTypes.AuthorizationCode,
            Permissions.GrantTypes.RefreshToken,
            Permissions.ResponseTypes.Code,
            Permissions.Scopes.Email,
            Permissions.Scopes.Profile,
        ]);
        descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
        descriptor.RedirectUris.UnionWith(client.RedirectUris.Select(uri => new Uri(uri, UriKind.Absolute)));
        descriptor.PostLogoutRedirectUris.UnionWith(client.PostLogoutRedirectUris.Select(uri => new Uri(uri, UriKind.Absolute)));
        descriptor.Properties[OidcClientProfile.AudienceProperty] = JsonSerializer.SerializeToElement(client.Audience ?? client.ClientId);
        descriptor.Properties[OidcClientProfile.SourceProperty] = JsonSerializer.SerializeToElement(OidcClientProfile.ConfigurationSource);
        if (client.PermissionNamespace is { } permissionNamespace)
            descriptor.Properties[OidcClientProfile.PermissionNamespaceProperty] = JsonSerializer.SerializeToElement(permissionNamespace);

        return descriptor;
    }

    /// <summary>Whether two registrations agree on everything but the secret.</summary>
    internal static bool IsSameRegistration(OpenIddictApplicationDescriptor current, OpenIddictApplicationDescriptor desired)
        => current.ApplicationType == desired.ApplicationType
            && current.ClientType == desired.ClientType
            && current.ConsentType == desired.ConsentType
            && current.DisplayName == desired.DisplayName
            && current.Permissions.SetEquals(desired.Permissions)
            && current.Requirements.SetEquals(desired.Requirements)
            && current.RedirectUris.SetEquals(desired.RedirectUris)
            && current.PostLogoutRedirectUris.SetEquals(desired.PostLogoutRedirectUris)
            && current.Properties.Count == desired.Properties.Count
            && desired.Properties.All(pair => current.Properties.TryGetValue(pair.Key, out var value) && value.GetRawText() == pair.Value.GetRawText());

    [LoggerMessage(EventId = 7111, Level = LogLevel.Information, Message = "OIDC client {ClientId} {Change} from configuration")]
    private static partial void LogSeeded(ILogger logger, string clientId, string change);
}
