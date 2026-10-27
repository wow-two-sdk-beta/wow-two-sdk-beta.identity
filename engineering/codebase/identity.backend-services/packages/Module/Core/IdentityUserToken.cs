using WoW.Two.Sdk.Beta.Be.Data.Abstractions;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>A token held for a user against one provider (composite key user + provider + name).</summary>
public sealed class IdentityUserToken : ICompositeKeyEntity
{
    /// <summary>The owning user.</summary>
    public Guid UserId { get; set; }

    /// <summary>Token provider (e.g. <c>Default</c>, <c>Authenticator</c>).</summary>
    public string LoginProvider { get; set; } = string.Empty;

    /// <summary>Token name (e.g. <c>ResetPassword</c>).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Token value.</summary>
    public string? Value { get; set; }
}
