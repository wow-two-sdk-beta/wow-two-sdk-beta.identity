namespace Identity.Infrastructure.AccountDeletion.Constants;

/// <summary>
/// Names what the service deletion route writes to the sign-in log: one row per call, with the caller's client id in
/// <c>client_id</c>, the person in <c>user_id</c>, and the outcome in <c>result</c>.
/// </summary>
public static class ServiceDeletionAuditConstants
{
    /// <summary>The <c>method</c> of every row the route writes.</summary>
    public const string Method = "service_account_deletion";

    /// <summary>The person was erased (<c>204</c>), or already was.</summary>
    public const string Erased = "erased";

    /// <summary>The caller's product authorizations were revoked and the person stays (<c>200</c>).</summary>
    public const string Kept = "kept";

    /// <summary>The person is not one of the caller's product's (<c>404</c>).</summary>
    public const string NotFound = "not_found";

    /// <summary>The person holds an organization role or a console permission (<c>409</c>).</summary>
    public const string Blocked = "blocked";

    /// <summary>The <c>reason</c> of a <see cref="Kept"/> row: the person has authorizations for other products' clients.</summary>
    public const string OtherProducts = "other_products";

    /// <summary>The <c>reason</c> of a <see cref="NotFound"/> row: no authorization for any of the caller's product clients.</summary>
    public const string NotProductPerson = "not_product_person";

    /// <summary>The <c>reason</c> of a <see cref="Blocked"/> row: the person is a member of an organization.</summary>
    public const string OrganizationRole = "organization_role";

    /// <summary>The <c>reason</c> of a <see cref="Blocked"/> row: the person holds an instance role, which opens the console.</summary>
    public const string ConsolePermission = "console_permission";
}
