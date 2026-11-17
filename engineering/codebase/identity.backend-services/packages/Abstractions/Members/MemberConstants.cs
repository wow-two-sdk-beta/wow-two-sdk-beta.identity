using System.Globalization;

namespace WoW.Two.Sdk.Beta.Be.Identity.Members;

/// <summary>Holds the member contract's route, scope and codes.</summary>
public static class MemberConstants
{
    /// <summary>The service-to-service route a Form 2 or 3 app reads an organization's members from; <c>{organizationId}</c> is a uuid.</summary>
    public const string Path = "/api/identity/v1/organizations/{organizationId}/members";

    /// <summary>The scope a client-credentials token needs on the route, granted per client in the instance's declaration.</summary>
    public const string Scope = "identity.members.read";

    /// <summary>The query parameter naming the one role to list.</summary>
    public const string RoleParameter = "role";

    /// <summary>The error metadata key holding the code, the problem's extension of the same name.</summary>
    public const string CodeMetadataKey = "identityCode";

    /// <summary>The code of a failure to reach the instance.</summary>
    public const string UnavailableCode = "MembersUnavailable";

    /// <summary>The route for <paramref name="organizationId"/>, with its leading slash.</summary>
    /// <param name="organizationId">The organization's id.</param>
    public static string PathOf(Guid organizationId)
        => Path.Replace("{organizationId}", organizationId.ToString("D", CultureInfo.InvariantCulture), StringComparison.Ordinal);
}
