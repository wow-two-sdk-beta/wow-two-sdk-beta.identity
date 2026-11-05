namespace WoW.Two.Sdk.Beta.Be.Identity.CurrentPrincipal;

/// <summary>Holds who is calling at one moment, the same shape whichever scheme authenticated them.</summary>
public sealed record NormalizedPrincipal : ICurrentPrincipal
{
    /// <summary>Gets the anonymous caller.</summary>
    public static NormalizedPrincipal Anonymous { get; } = new() { Kind = PrincipalKind.Anonymous };

    /// <inheritdoc />
    public string? Subject { get; init; }

    /// <inheritdoc />
    public required PrincipalKind Kind { get; init; }

    /// <inheritdoc />
    public string? Issuer { get; init; }

    /// <inheritdoc />
    public string? ClientId { get; init; }

    /// <inheritdoc />
    public string? OrganizationId { get; init; }

    /// <inheritdoc />
    public IReadOnlySet<string> Roles { get; init; } = new HashSet<string>();

    /// <inheritdoc />
    public IReadOnlySet<string> Permissions { get; init; } = new HashSet<string>();

    /// <inheritdoc />
    public IReadOnlySet<string> Scopes { get; init; } = new HashSet<string>();

    /// <inheritdoc />
    public ActorInfo? Actor { get; init; }

    /// <inheritdoc />
    public string? Partition { get; init; }
}
