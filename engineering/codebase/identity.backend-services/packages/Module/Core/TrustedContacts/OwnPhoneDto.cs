namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TrustedContacts;

/// <summary>Answers <c>GET manage/phone</c>: the account's own phone and whether this session may set it.</summary>
public sealed record OwnPhoneDto
{
    /// <summary>The account's masked phone (<c>+998*******45</c>); null without one.</summary>
    public string? PhoneHint { get; init; }

    /// <summary>Whether the phone is confirmed.</summary>
    public required bool IsConfirmed { get; init; }

    /// <summary>Whether this session may add or change it: any session adds a first phone, only an own-phone session changes one.</summary>
    public required bool CanChange { get; init; }
}
