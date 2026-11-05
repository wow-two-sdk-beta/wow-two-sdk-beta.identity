using System.Text.Json;
using OpenIddict.Abstractions;

namespace WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

/// <summary>What the token claims need to know of a client: its audience and permission namespace, read from the registry.</summary>
/// <param name="ClientId">The client id.</param>
/// <param name="Audience">The audience its access tokens carry.</param>
/// <param name="PermissionNamespace">The namespace its <c>org_permission</c> values are filtered to; null keeps all.</param>
internal sealed record OidcClientProfile(string ClientId, string Audience, string? PermissionNamespace)
{
    /// <summary>Gets what the client may ask for at the token endpoint beyond the code flow.</summary>
    public OidcClientServiceAccess ServiceAccess { get; init; } = new();

    /// <summary>The application property holding the audience.</summary>
    public const string AudienceProperty = "audience";

    /// <summary>The application property holding the permission namespace.</summary>
    public const string PermissionNamespaceProperty = "permission_namespace";

    /// <summary>The application property holding <see cref="OidcClientServiceAccess"/>.</summary>
    public const string ServiceAccessProperty = "service_access";

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
        return new OidcClientProfile(clientId, Read(properties, AudienceProperty) ?? clientId, Read(properties, PermissionNamespaceProperty))
        {
            ServiceAccess = properties.TryGetValue(ServiceAccessProperty, out var access) && access.ValueKind == JsonValueKind.Object
                ? access.Deserialize<OidcClientServiceAccess>() ?? new OidcClientServiceAccess()
                : new OidcClientServiceAccess(),
        };
    }

    private static string? Read(IReadOnlyDictionary<string, JsonElement> properties, string name)
        => properties.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>What a client may ask for at the token endpoint beyond the code flow, kept in the registry beside its profile.</summary>
internal sealed record OidcClientServiceAccess
{
    /// <summary>Gets the resource URIs it may call as itself.</summary>
    public IReadOnlyList<string> CredentialResources { get; init; } = [];

    /// <summary>Gets the resource URIs it may call for a person.</summary>
    public IReadOnlyList<string> ExchangeResources { get; init; } = [];

    /// <summary>Gets the scopes it may ask for.</summary>
    public IReadOnlyList<string> Scopes { get; init; } = [];

    /// <summary>Gets the organization its own tokens carry, or null.</summary>
    public Guid? OrganizationId { get; init; }
}
