namespace WoW.Two.Sdk.Beta.Be.Identity.Keys;

/// <summary>Holds where the OIDC server's certificates come from: settings per role, or ephemeral keys on a local run.</summary>
/// <remarks>
/// Bound from <c>Identity:Server:Keys</c>. Each entry is an inline PEM bundle (certificate and unencrypted PKCS#8 private
/// key, as <c>keys generate</c> prints it), which a vault secret carries, or the path of a file holding one.
/// </remarks>
public sealed record ServerKeyOptions
{
    /// <summary>Gets or sets the signing certificates, each an inline PEM bundle or a file path; several stand side by side during a rotation.</summary>
    /// <remarks>A relative path resolves against the host's content root.</remarks>
    public List<string> Signing { get; set; } = [];

    /// <summary>Gets or sets the encryption certificates, each an inline PEM bundle or a file path; an old one stays until the last token it sealed expires.</summary>
    /// <remarks>A relative path resolves against the host's content root.</remarks>
    public List<string> Encryption { get; set; } = [];

    /// <summary>Gets or sets whether the server makes throwaway keys at boot instead of reading its settings; only a local run may.</summary>
    /// <remarks>Every restart voids each issued token. A deployed boot (<c>DEPLOY_ENVIRONMENT</c> set, or not <c>Development</c>) refuses it.</remarks>
    public bool Ephemeral { get; set; }
}
