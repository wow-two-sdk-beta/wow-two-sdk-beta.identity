namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Sessions;

/// <summary>Represents one live session: its <c>sid</c> and its decrypted state.</summary>
internal sealed record BffSession
{
    /// <summary>Gets the session's <c>sid</c>, the store key and the antiforgery binding.</summary>
    public required string SessionId { get; init; }

    /// <summary>Gets the session's claims and tokens.</summary>
    public required BffSessionState State { get; init; }
}
