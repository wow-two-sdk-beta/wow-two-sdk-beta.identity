namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Phones;

/// <summary>
/// Holds the countries a phone number may come from and their dialling codes: the same list, in the same order, as the
/// fe sdk's <c>PhoneInput</c> offers, so a number the form builds is one the server knows. Several countries may share a
/// code (<c>+1</c>, <c>+7</c>); a number belongs to each country whose code is its longest matching prefix.
/// </summary>
public static class PhoneCountryConstants
{
    /// <summary>ISO 3166-1 alpha-2 code → dialling code without <c>+</c>.</summary>
    public static readonly IReadOnlyDictionary<string, string> DialCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
            ["US"] = "1",
            ["CA"] = "1",
            ["GB"] = "44",
            ["AU"] = "61",
            ["DE"] = "49",
            ["FR"] = "33",
            ["ES"] = "34",
            ["IT"] = "39",
            ["NL"] = "31",
            ["BE"] = "32",
            ["CH"] = "41",
            ["AT"] = "43",
            ["SE"] = "46",
            ["NO"] = "47",
            ["DK"] = "45",
            ["FI"] = "358",
            ["IE"] = "353",
            ["PT"] = "351",
            ["PL"] = "48",
            ["CZ"] = "420",
            ["GR"] = "30",
            ["TR"] = "90",
            ["RU"] = "7",
            ["UA"] = "380",
            ["UZ"] = "998",
            ["KZ"] = "7",
            ["KG"] = "996",
            ["TJ"] = "992",
            ["TM"] = "993",
            ["IL"] = "972",
            ["AE"] = "971",
            ["SA"] = "966",
            ["EG"] = "20",
            ["ZA"] = "27",
            ["NG"] = "234",
            ["KE"] = "254",
            ["IN"] = "91",
            ["PK"] = "92",
            ["BD"] = "880",
            ["SG"] = "65",
            ["MY"] = "60",
            ["TH"] = "66",
            ["VN"] = "84",
            ["PH"] = "63",
            ["ID"] = "62",
            ["JP"] = "81",
            ["KR"] = "82",
            ["CN"] = "86",
            ["HK"] = "852",
            ["TW"] = "886",
            ["NZ"] = "64",
            ["BR"] = "55",
            ["AR"] = "54",
            ["CL"] = "56",
            ["CO"] = "57",
            ["MX"] = "52",
    };
}
