namespace WoW.Two.Sdk.Beta.Be.Identity.Core.Endpoints;

/// <summary>Who may create an account.</summary>
public enum SignUpMode
{
    /// <summary>
    /// Only a person holding an invitation link signs up, joining its organization with its role; an admin adds everyone
    /// else. The hosted pages offer no sign-up form without a link.
    /// </summary>
    Invite,

    /// <summary>Anyone signs up through <c>sign-up</c> or a first phone sign-in; an invitation link still adds its role.</summary>
    Open,
}
