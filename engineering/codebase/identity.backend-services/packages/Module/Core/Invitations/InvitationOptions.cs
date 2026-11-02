namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Invitations;

/// <summary>Holds how long an invitation stays redeemable and where its link leads.</summary>
/// <remarks>Set in code with <c>.AddInvitations(o => …)</c> or in the host section <c>Identity:Invitations</c>, which is applied last.</remarks>
public sealed record InvitationOptions
{
    /// <summary>The host configuration section.</summary>
    public const string SectionName = "Identity:Invitations";

    /// <summary>Gets or sets how long an invitation stays redeemable from its making. Default 7 days.</summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Gets or sets the link an invitation hands out, with <c>{token}</c> standing for its token, such as
    /// <c>https://id.example.com/sign-up?invitation={token}</c>; null hands out the token alone. The service sets it from
    /// <c>Identity:Server:Issuer</c>.
    /// </summary>
    public string? LinkTemplate { get; set; }
}
