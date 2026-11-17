namespace Identity.Api.Settings;

/// <summary>Names the resource audience of the app notice route; the Module's <c>AppNoticeOptions</c> share the section.</summary>
public sealed record AppNoticeSettings
{
    /// <summary>The host section.</summary>
    public const string SectionName = "Identity:Notices";

    /// <summary>
    /// Gets the absolute HTTPS URI of the resource whose tokens the route accepts: registered under
    /// <c>Identity:Server:Resources</c> with the scope <c>identity.notices.send</c>. Null maps no route.
    /// </summary>
    public Uri? Audience { get; init; }
}
