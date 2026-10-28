namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>
/// One key of the instance's ASP.NET Core Data Protection ring, as the key manager writes it: an XML element, wrapped by
/// the ring certificate when one is configured. Written and read only by the Server package's ring repository.
/// </summary>
public sealed class IdentityDataProtectionKey
{
    /// <summary>Creates a key row with a time-ordered id, so the ring reads in the order it was written.</summary>
    public IdentityDataProtectionKey() => Id = Guid.CreateVersion7();

    /// <summary>Primary key, a Guid v7 made in code.</summary>
    public Guid Id { get; set; }

    /// <summary>The key manager's name for the element, such as <c>key-{guid}</c>.</summary>
    public string? FriendlyName { get; set; }

    /// <summary>The key's XML; its secret part is encrypted to the ring certificate outside a local run.</summary>
    public string Xml { get; set; } = string.Empty;
}
