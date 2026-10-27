namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>Names the kinds of host that claim the identity schema in the migrator's owner marker.</summary>
public static class IdentitySchemaOwnerConstants
{
    /// <summary>A Form 1 product hosting the Module over its own database.</summary>
    public const string Module = "module";

    /// <summary>The Identity service, an instance serving Forms 2 and 3.</summary>
    public const string Service = "service";
}
