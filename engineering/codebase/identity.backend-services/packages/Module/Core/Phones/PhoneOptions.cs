namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Phones;

/// <summary>Holds which phone numbers the instance accepts.</summary>
/// <remarks>
/// Set in code with <c>.AddPhoneNumbers(o => …)</c> or in the host section <c>Identity:Phone</c>, which is applied last and
/// read live. The same list feeds the fe sdk's <c>PhoneInput</c> through the sign-in declaration.
/// </remarks>
public sealed record PhoneOptions
{
    /// <summary>The host configuration section.</summary>
    public const string SectionName = "Identity:Phone";

    /// <summary>
    /// Gets or sets the ISO 3166-1 alpha-2 codes of the countries numbers may come from, such as <c>UZ</c>; each must be
    /// one <see cref="PhoneCountryConstants.DialCodes"/> knows. Empty allows every country it knows. Default empty.
    /// </summary>
    public List<string> AllowedCountries { get; set; } = [];
}
