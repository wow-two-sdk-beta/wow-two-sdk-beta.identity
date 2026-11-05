namespace WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;

/// <summary>Represents a live API key as a repository answers a lookup — enough to authenticate and to record use.</summary>
public sealed record ApiKeyRecord
{
    /// <summary>The key's identifier, as the repository names it.</summary>
    public required string Id { get; init; }

    /// <summary>The name its owner gave the key, carried as the principal's name.</summary>
    public required string Name { get; init; }

    /// <summary>When the key last authenticated a request, or <c>null</c> before its first use.</summary>
    public DateTimeOffset? LastUsedAt { get; init; }

    /// <summary>The scopes the key grants, each carried as a <c>scope</c> claim; a key with none reaches only
    /// endpoints that ask for no scope.</summary>
    public IReadOnlyList<string> Scopes { get; init; } = [];

    /// <summary>The app the key belongs to, carried as <c>client_id</c>; null names none.</summary>
    public string? ClientId { get; init; }

    /// <summary>The organization the key is bound to, carried as <c>org_id</c>; null names none.</summary>
    public string? OrganizationId { get; init; }

    /// <summary>The tenant the key belongs to, carried as <c>partition</c>; null names none.</summary>
    public string? Partition { get; init; }

    /// <summary>When the key stops working; null never.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
}
