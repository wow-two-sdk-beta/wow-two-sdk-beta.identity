namespace Identity.Api.Settings;

/// <summary>Names the resource audience of the organization members route.</summary>
public sealed record MemberSettings
{
    /// <summary>The host section.</summary>
    public const string SectionName = "Identity:Members";

    /// <summary>
    /// Gets the absolute HTTPS URI of the resource whose tokens the route accepts: registered under
    /// <c>Identity:Server:Resources</c> with the scope <c>identity.members.read</c>, such as the same resource the app notice
    /// route names. Null maps no route.
    /// </summary>
    public Uri? Audience { get; init; }
}
