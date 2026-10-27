using System.Text.Json;
using OpenIddict.Abstractions;

namespace WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

/// <summary>What the token claims need to know of a client: its audience and permission namespace, read from the registry.</summary>
/// <param name="ClientId">The client id.</param>
/// <param name="Audience">The audience its access tokens carry.</param>
/// <param name="PermissionNamespace">The namespace its <c>org_permission</c> values are filtered to; null keeps all.</param>
internal sealed record OidcClientProfile(string ClientId, string Audience, string? PermissionNamespace)
{
    /// <summary>The application property holding the audience.</summary>
    public const string AudienceProperty = "audience";

    /// <summary>The application property holding the permission namespace.</summary>
    public const string PermissionNamespaceProperty = "permission_namespace";

    /// <summary>The application property naming who registered the client; the seeder owns <see cref="ConfigurationSource"/> rows.</summary>
    public const string SourceProperty = "source";

    /// <summary>The source value of clients seeded from <c>Identity:Server:Clients</c>.</summary>
    public const string ConfigurationSource = "configuration";

    /// <summary>The profile of <paramref name="clientId"/>, or null when no such client is registered.</summary>
    /// <param name="applications">The OpenIddict application manager.</param>
    /// <param name="clientId">The client id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<OidcClientProfile?> FindAsync(IOpenIddictApplicationManager applications, string? clientId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(clientId) || await applications.FindByClientIdAsync(clientId, cancellationToken) is not { } application)
            return null;

        var properties = await applications.GetPropertiesAsync(application, cancellationToken);
        return new OidcClientProfile(clientId, Read(properties, AudienceProperty) ?? clientId, Read(properties, PermissionNamespaceProperty));
    }

    private static string? Read(IReadOnlyDictionary<string, JsonElement> properties, string name)
        => properties.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
