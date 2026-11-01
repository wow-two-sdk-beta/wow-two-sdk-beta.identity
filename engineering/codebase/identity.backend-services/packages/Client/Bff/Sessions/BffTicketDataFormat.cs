using Microsoft.AspNetCore.Authentication;

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Sessions;

/// <summary>
/// Formats the session cookie so it carries only the session reference: whatever principal or tokens a ticket holds when
/// the cookie is written (a sign-in, a renewal), only the <c>sid</c>, the sign-in's reference and the ticket's issue,
/// expiry and persistence reach the cookie, protected by <paramref name="inner"/>.
/// </summary>
/// <param name="inner">The Data Protection ticket format.</param>
internal sealed class BffTicketDataFormat(ISecureDataFormat<AuthenticationTicket> inner) : ISecureDataFormat<AuthenticationTicket>
{
    /// <summary>Holds the property items a cookie keeps besides the reference.</summary>
    private static readonly string[] KeptItems = [".issued", ".expires", ".persistent", ".allowrefresh"];

    /// <inheritdoc />
    public string Protect(AuthenticationTicket data) => inner.Protect(ToReferenceTicket(data));

    /// <inheritdoc />
    public string Protect(AuthenticationTicket data, string? purpose) => inner.Protect(ToReferenceTicket(data), purpose);

    /// <inheritdoc />
    public AuthenticationTicket? Unprotect(string? protectedText) => inner.Unprotect(protectedText);

    /// <inheritdoc />
    public AuthenticationTicket? Unprotect(string? protectedText, string? purpose) => inner.Unprotect(protectedText, purpose);

    /// <summary>The cookie's form of <paramref name="ticket"/>: the reference and the cookie's lifetime, no claim and no token.</summary>
    /// <param name="ticket">The ticket being written.</param>
    internal static AuthenticationTicket ToReferenceTicket(AuthenticationTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var reference = BffSessionReference.From(ticket.Properties)
            ?? throw new InvalidOperationException("A BFF session cookie is written only for a session the sign-in stored; this ticket names none.");

        var items = KeptItems
            .Where(ticket.Properties.Items.ContainsKey)
            .ToDictionary(key => key, key => ticket.Properties.Items[key], StringComparer.Ordinal);
        var properties = new AuthenticationProperties(items);
        reference.WriteTo(properties);
        return new AuthenticationTicket(BffSessionService.ReferencePrincipal(), properties, ticket.AuthenticationScheme);
    }
}
