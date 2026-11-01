using WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core;

/// <summary>Holds password rules (consumed by the password slice). Defaults follow NIST SP 800-63B: length over composition.</summary>
/// <remarks>Set in code with <c>AddUserAccounts(o =&gt; o.Password…)</c> or in the host section <c>Identity:Password</c>, which is applied last.</remarks>
public sealed record PasswordOptions
{
    /// <summary>The host configuration section.</summary>
    public const string SectionName = "Identity:Password";

    /// <summary>Minimum password length. Default 15.</summary>
    public int MinLength { get; set; } = 15;

    /// <summary>Maximum password length; bounds the hashing cost a caller can force. Default 128.</summary>
    public int MaxLength { get; set; } = 128;

    /// <summary>Require an ASCII digit. Default false.</summary>
    public bool IsDigitRequired { get; set; }

    /// <summary>Require a lowercase letter. Default false.</summary>
    public bool IsLowercaseRequired { get; set; }

    /// <summary>Require an uppercase letter. Default false.</summary>
    public bool IsUppercaseRequired { get; set; }

    /// <summary>Require a character that is neither a letter nor a digit. Default false.</summary>
    public bool IsNonAlphanumericRequired { get; set; }

    /// <summary>Minimum number of distinct characters. Default 1.</summary>
    public int RequiredUniqueChars { get; set; } = 1;

    /// <summary>Reject a password equal to the account's user name or email, ignoring case. Default true.</summary>
    public bool IsAccountIdentifierRejected { get; set; } = true;

    /// <summary>How a signed-in person's password change is confirmed (<c>Identity:Password:Change</c>).</summary>
    public PasswordChangeOptions Change { get; } = new();
}
