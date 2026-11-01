namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Endpoints;

/// <summary>Represents a finished local logout, as <c>POST {prefix}/logout</c> answers it.</summary>
public sealed record BffLogoutDto
{
    /// <summary>Gets the instance's end-session URL the SPA sends the browser to, ending the instance's session too.</summary>
    public required string EndSessionUrl { get; init; }
}
