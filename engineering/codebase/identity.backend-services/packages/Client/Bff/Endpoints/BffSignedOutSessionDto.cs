namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Endpoints;

/// <summary>Represents no session, as <c>GET {prefix}/session</c> answers it.</summary>
public sealed record BffSignedOutSessionDto
{
    /// <summary>Gets whether a session is live; always <c>false</c> here.</summary>
    public bool IsAuthenticated => false;
}
