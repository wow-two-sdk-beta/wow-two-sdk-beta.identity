namespace Identity.Api.AccountDeletion;

/// <summary>How one call of the service deletion route ended, in the order its branches are decided.</summary>
public enum ServiceDeletionOutcome
{
    /// <summary>The person never authorized any of the caller's product clients, so no such person exists for that product: <c>404</c>.</summary>
    NotThisProductsPerson,

    /// <summary>The person holds an organization role or an instance role (a console permission): <c>409</c>.</summary>
    HoldsAccess,

    /// <summary>The person also uses other products: the caller's product authorizations and tokens were revoked and the person stays: <c>200</c>.</summary>
    Kept,

    /// <summary>The person was erased, or already was: <c>204</c>.</summary>
    Erased,
}
