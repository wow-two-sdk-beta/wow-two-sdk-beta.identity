namespace WoW.Two.Sdk.Beta.Be.Identity.Core.PhoneSignIn;

/// <summary>Refers to how a phone sign-in step ended.</summary>
public enum PhoneSignInStatus
{
    /// <summary>The code went to the number's linked chat.</summary>
    CodeSent,

    /// <summary>The number has no reachable chat yet: the person opens the start link, and the code follows the shared contact.</summary>
    LinkRequired,

    /// <summary>The code verified and the person is known or was just created.</summary>
    Verified,

    /// <summary>The input is no E.164 number.</summary>
    InvalidNumber,

    /// <summary>The number belongs to no allowed country.</summary>
    CountryNotAllowed,

    /// <summary>A code or link went out moments ago for this number or from this address.</summary>
    RateLimited,

    /// <summary>The code is wrong, spent, expired or out of attempts.</summary>
    InvalidCode,

    /// <summary>The code verified, no account holds the number, and sign-up is off.</summary>
    SignUpClosed,

    /// <summary>The person holding the number is locked out; the code was not checked, so it is not spent.</summary>
    LockedOut,
}
