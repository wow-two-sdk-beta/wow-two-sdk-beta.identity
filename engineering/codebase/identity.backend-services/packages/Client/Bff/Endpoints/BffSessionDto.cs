namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Endpoints;

/// <summary>Represents a live session as <c>GET {prefix}/session</c> answers it.</summary>
public sealed record BffSessionDto
{
    /// <summary>Gets whether a session is live; always <c>true</c> here.</summary>
    public bool IsAuthenticated => true;

    /// <summary>Gets the signed-in person.</summary>
    public required BffPersonDto Person { get; init; }

    /// <summary>Gets the active organization, or null for a person without a membership.</summary>
    public required BffOrganizationDto? Organization { get; init; }

    /// <summary>Gets the active organization's permissions, without the <c>{org_id}:</c> prefix, such as <c>desk:edit</c>.</summary>
    public required IReadOnlyList<string> Permissions { get; init; }

    /// <summary>Gets the antiforgery token an unsafe API request echoes in <c>X-Csrf-Token</c>.</summary>
    public required string CsrfToken { get; init; }

    /// <summary>Gets the absolute end of the session (UTC).</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}
