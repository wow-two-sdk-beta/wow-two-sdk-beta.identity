namespace WoW.Two.Sdk.Beta.Be.Identity.Testing;

/// <summary>Holds the person a <see cref="FakeTokenIssuer"/> signs a token for.</summary>
public sealed record FakePerson
{
    /// <summary>Gets the person's id, the token's <c>sub</c>.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the active organization (<c>org_id</c>); null writes no organization claims.</summary>
    public Guid? OrganizationId { get; init; }

    /// <summary>
    /// Gets the role in the active organization, written as <c>org_role</c> <c>{org}:{role}</c>; it needs
    /// <see cref="OrganizationId"/>.
    /// </summary>
    public string? Role { get; init; }

    /// <summary>
    /// Gets the permissions in the active organization, written as <c>org_permission</c> <c>{org}:{permission}</c> in a JSON
    /// array; they need <see cref="OrganizationId"/>.
    /// </summary>
    public IReadOnlyList<string> Permissions { get; init; } = [];

    /// <summary>Gets the sign-in session (<c>sid</c>); null writes none.</summary>
    public Guid? SessionId { get; init; }
}
