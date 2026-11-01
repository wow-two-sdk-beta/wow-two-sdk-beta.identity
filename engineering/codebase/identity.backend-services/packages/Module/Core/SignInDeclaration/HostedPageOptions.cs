namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInDeclaration;

/// <summary>Holds the instance's look on the hosted pages: the owner's brand name, the fe sdk theme and an optional background.</summary>
/// <remarks>
/// Set in code with <c>.AddSignInDeclaration(o => …)</c> or in the host section <c>Identity:Pages</c>, which is applied last
/// and read live. An instance serves one product, so it has one look: no app overrides it. A value invalid on its own stops
/// the start.
/// </remarks>
public sealed record HostedPageOptions
{
    /// <summary>The host configuration section.</summary>
    public const string SectionName = "Identity:Pages";

    /// <summary>The theme the pages take when no setting names one: the shared look of the platform apps.</summary>
    public const string DefaultTheme = "platform";

    /// <summary>Gets or sets the name the pages show, the instance owner's brand. Default <c>Identity</c>.</summary>
    public string BrandName { get; set; } = "Identity";

    /// <summary>Gets or sets the fe sdk theme by its registry id, such as <c>platform</c> or <c>ocharo</c>. Default <see cref="DefaultTheme"/>.</summary>
    public string Theme { get; set; } = DefaultTheme;

    /// <summary>
    /// Gets or sets the image behind the pages' card: a path on the instance's own origin, such as
    /// <c>/backgrounds/ocharo.jpg</c>, or an absolute HTTPS URL. Null or empty shows the theme's own backdrop.
    /// </summary>
    public string? BackgroundUrl { get; set; }
}
