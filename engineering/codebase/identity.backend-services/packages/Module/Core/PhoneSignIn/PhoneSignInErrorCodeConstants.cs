namespace WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;

/// <summary>Names the phone sign-in failures the account API reports in <c>identityCode</c> and <c>messageKey</c>, beside the contract's codes.</summary>
public static class PhoneSignInErrorCodeConstants
{
    /// <summary>The phone is no E.164 number.</summary>
    public const string InvalidPhoneNumber = "InvalidPhoneNumber";

    /// <summary>The phone belongs to no allowed country (<c>Identity:Phone:AllowedCountries</c>).</summary>
    public const string PhoneCountryNotAllowed = "PhoneCountryNotAllowed";

    /// <summary>A code or link went out moments ago for this number or from this address.</summary>
    public const string PhoneCodeRateLimited = "PhoneCodeRateLimited";

    /// <summary>Phone sign-in is off: the instance's bot does not run.</summary>
    public const string PhoneSignInUnavailable = "PhoneSignInUnavailable";
}
