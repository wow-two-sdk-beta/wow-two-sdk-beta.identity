using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>An external account (Google/Microsoft/Telegram/…) linked to a user (composite key provider + provider-key).</summary>
/// <remarks>Stored in <c>identity_user_logins</c>, whose provider column keeps its <c>001</c> name <c>login_provider</c>.</remarks>
public sealed class IdentityExternalAccount : ICompositeKeyEntity
{
    /// <summary>External provider name (e.g. <c>Google</c>).</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>The user's id at the provider.</summary>
    public string ProviderKey { get; set; } = string.Empty;

    /// <summary>Friendly provider name for display.</summary>
    public string? ProviderDisplayName { get; set; }

    /// <summary>The local user this external account belongs to.</summary>
    public Guid UserId { get; set; }
}
