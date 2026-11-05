namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Signals;

/// <summary>Refers to what changed in a person's access to an organization.</summary>
public enum AccessChangeKind
{
    /// <summary>They joined, or their role changed: the next token carries it, and work under way goes on.</summary>
    Changed,

    /// <summary>They left, or the organization was deleted: that access ended, and work under it stops.</summary>
    Removed,
}
