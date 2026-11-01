namespace WoW.Two.Sdk.Beta.Be.Identity.Core.SignInDeclaration;

/// <summary>Represents what a registered app overrides of the instance's look; a null or empty part keeps the instance's.</summary>
public sealed record HostedPageLook
{
    /// <summary>Gets the fe sdk theme by its registry id, such as <c>ocharo</c>.</summary>
    public string? Theme { get; init; }

    /// <summary>Gets the image behind the card: a same-origin path or an absolute HTTPS URL.</summary>
    public string? BackgroundUrl { get; init; }
}
