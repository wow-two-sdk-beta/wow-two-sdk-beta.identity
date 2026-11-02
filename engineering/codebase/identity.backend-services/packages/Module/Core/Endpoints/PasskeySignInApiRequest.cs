using System.Text.Json;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

/// <summary>Completes a passkey sign-in with the assertion the browser signed.</summary>
public sealed record PasskeySignInApiRequest
{
    /// <summary>The state from <c>passkeys/sign-in/options</c>.</summary>
    public required string State { get; init; }

    /// <summary>The <c>PublicKeyCredential</c> from <c>navigator.credentials.get</c>, as <c>toJSON()</c> renders it.</summary>
    public required JsonElement Credential { get; init; }

    /// <summary>
    /// Whether the person asked to stay signed in ("Keep me signed in"): a fixed session then takes
    /// <c>Identity:Sessions:ExtendedLifetime</c> and a cookie that outlives the browser; ignored while sessions slide or no
    /// extended lifetime is set (<c>sign-in-options</c>' <c>isPersistenceOffered</c>).
    /// </summary>
    public bool IsPersistent { get; init; }
}
