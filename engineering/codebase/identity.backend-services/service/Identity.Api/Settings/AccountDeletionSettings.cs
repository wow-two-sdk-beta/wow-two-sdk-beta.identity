namespace Identity.Api.Settings;

/// <summary>
/// Names the service clients allowed to delete accounts remotely, the products each one owns, and the route's dedicated
/// audience. A deletion client reaches only a person who authorized one of its product's clients.
/// </summary>
public sealed record AccountDeletionSettings
{
    /// <summary>The host section for the products' deletion clients.</summary>
    public const string SectionName = "Identity:AccountDeletion";

    /// <summary>Gets the absolute HTTPS resource audience dedicated to account deletion; null maps no route.</summary>
    public Uri? Audience { get; init; }

    /// <summary>Gets the deletion clients by client identifier; the route answers <c>403</c> to a client not listed here.</summary>
    public Dictionary<string, AccountDeletionClientSettings> Clients { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>Names the native or web clients one product owns, for the deletion client that serves that product.</summary>
public sealed record AccountDeletionClientSettings
{
    /// <summary>Gets the client identifiers of the product's sign-in clients, as registered under <c>Identity:Server:Clients</c>.</summary>
    public string[] ProductClientIds { get; init; } = [];
}
