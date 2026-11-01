namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Sessions;

/// <summary>
/// Represents one stored BFF session, the shape every <see cref="IBffSessionRepository"/> maps onto: the session's id,
/// its absolute end, and its tokens and claims encrypted with Data Protection.
/// </summary>
public sealed record BffSessionRecord
{
    /// <summary>Gets the session's <c>sid</c>, as the instance issued it; the key the store uses.</summary>
    public required string SessionId { get; init; }

    /// <summary>Gets the absolute end of the session (UTC); a store may drop the record after it.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Gets the encrypted tokens and claims; opaque to the store.</summary>
    public required byte[] Payload { get; init; }
}
