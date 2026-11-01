namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Sessions;

/// <summary>Refers to how a refresh at the instance's token endpoint ended.</summary>
internal enum BffTokenRefreshStatus
{
    /// <summary>The instance issued new tokens.</summary>
    Refreshed,

    /// <summary>The instance refused the grant (revoked, past its window, the person locked out) or sent an ID token that
    /// failed validation: the session ends.</summary>
    Rejected,

    /// <summary>The instance could not be reached or failed: the session keeps its tokens and tries again later.</summary>
    Unavailable,
}
