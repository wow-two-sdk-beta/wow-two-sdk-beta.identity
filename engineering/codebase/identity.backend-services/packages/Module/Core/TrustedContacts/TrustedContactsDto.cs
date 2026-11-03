namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>Answers <c>GET manage/trusted-contacts</c>: the account's contacts, and whether this session may revoke them.</summary>
public sealed record TrustedContactsDto
{
    /// <summary>Whether this session was signed in with the account's own phone, which revoking a contact needs.</summary>
    public required bool CanRevoke { get; init; }

    /// <summary>The contacts, oldest first.</summary>
    public required IReadOnlyList<TrustedContactDto> Items { get; init; }
}
