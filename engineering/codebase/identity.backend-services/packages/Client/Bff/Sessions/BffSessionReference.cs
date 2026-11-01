using Microsoft.AspNetCore.Authentication;

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Sessions;

/// <summary>
/// Represents the session reference, the only thing the cookie carries: the <c>sid</c> and the sign-in's random
/// reference, kept as two items of the cookie ticket's properties.
/// </summary>
internal sealed record BffSessionReference
{
    /// <summary>Holds the properties item that carries the <c>sid</c>.</summary>
    public const string SessionIdItem = ".bff.sid";

    /// <summary>Holds the properties item that carries the sign-in's reference.</summary>
    public const string ReferenceItem = ".bff.ref";

    /// <summary>Gets the session's <c>sid</c>.</summary>
    public required string SessionId { get; init; }

    /// <summary>Gets the sign-in's random reference.</summary>
    public required string Reference { get; init; }

    /// <summary>The reference <paramref name="properties"/> carry, or null when they carry none.</summary>
    /// <param name="properties">A cookie ticket's properties.</param>
    public static BffSessionReference? From(AuthenticationProperties? properties)
        => properties?.Items.TryGetValue(SessionIdItem, out var sessionId) == true && !string.IsNullOrEmpty(sessionId)
            && properties.Items.TryGetValue(ReferenceItem, out var reference) && !string.IsNullOrEmpty(reference)
                ? new BffSessionReference { SessionId = sessionId, Reference = reference }
                : null;

    /// <summary>Write this reference into <paramref name="properties"/>.</summary>
    /// <param name="properties">A cookie ticket's properties.</param>
    public void WriteTo(AuthenticationProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        properties.Items[SessionIdItem] = SessionId;
        properties.Items[ReferenceItem] = Reference;
    }
}
