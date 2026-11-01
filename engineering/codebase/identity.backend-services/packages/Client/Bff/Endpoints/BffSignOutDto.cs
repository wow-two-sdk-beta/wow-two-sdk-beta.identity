namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Endpoints;

/// <summary>Represents a finished local sign-out, as <c>POST {prefix}/sign-out</c> answers it.</summary>
public sealed record BffSignOutDto
{
    /// <summary>Gets the instance's end-session URL the SPA sends the browser to, ending the instance's session too.</summary>
    public required string EndSessionUrl { get; init; }
}
