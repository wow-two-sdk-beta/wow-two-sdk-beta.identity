namespace Identity.Api.Settings;

/// <summary>Holds the purpose-token settings bound from <c>Identity:Tokens</c>: the signing key and the token lifetimes.</summary>
public sealed record TokenSettings
{
    /// <summary>The host section the settings bind from.</summary>
    public const string SectionName = "Identity:Tokens";

    /// <summary>Gets the HMAC key that signs the confirmation, reset and forced-change tokens: base64 or base64url of at least 32 bytes, or the path of a file holding it. Every instance on one database needs the same value; a secret.</summary>
    public string? SigningKey { get; init; }

    /// <summary>Gets whether the instance draws a throwaway key at start when no key is set. Only a local run may set it.</summary>
    public bool IsKeyEphemeral { get; init; }

    /// <summary>Gets the lifetime of a purpose without an entry in <see cref="Lifetimes"/>, such as <c>1.00:00:00</c>; unset keeps the module's default of 1 day.</summary>
    public TimeSpan? DefaultLifetime { get; init; }

    /// <summary>Gets the lifetime per bare purpose, such as <c>PasswordReset</c>; an entry replaces the module's default for that purpose.</summary>
    public Dictionary<string, TimeSpan> Lifetimes { get; init; } = [];

    /// <summary>Gets whether a key is set.</summary>
    internal bool HasKey => !string.IsNullOrWhiteSpace(SigningKey);
}
