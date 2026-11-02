using System.Globalization;
using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

namespace WoW.Two.Sdk.Beta.Be.Identity.Bff.Endpoints;

/// <summary>Maps a session's principal to the session route's answer.</summary>
internal static class BffSessionMapper
{
    /// <summary>The session route's answer for <paramref name="principal"/>.</summary>
    /// <param name="principal">The session's person.</param>
    /// <param name="csrfToken">The session's antiforgery token.</param>
    /// <param name="expiresAt">The session's absolute end.</param>
    public static BffSessionDto ToDto(ClaimsPrincipal principal, string csrfToken, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var organization = Guid.TryParse(principal.FindFirst(OrganizationClaimTypeConstants.Id)?.Value, CultureInfo.InvariantCulture, out var id) ? id : (Guid?)null;
        var prefix = organization?.ToString("D", CultureInfo.InvariantCulture) + ":";

        return new BffSessionDto
        {
            Person = new BffPersonDto
            {
                Sub = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? string.Empty,
                Name = principal.FindFirst(JwtRegisteredClaimNames.Name)?.Value,
                Picture = principal.FindFirst(JwtRegisteredClaimNames.Picture)?.Value,
                Email = principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value,
                PhoneNumber = principal.FindFirst(JwtRegisteredClaimNames.PhoneNumber)?.Value,
            },
            Organization = organization is { } active
                ? new BffOrganizationDto { Id = active, Role = principal.GetOrganizationRole(active) }
                : null,
            Permissions = organization is null
                ? []
                : [.. principal.FindAll(OrganizationClaimTypeConstants.Permission)
                    .Select(claim => claim.Value)
                    .Where(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .Select(value => value[prefix.Length..])
                    .Distinct(StringComparer.Ordinal)],
            CsrfToken = csrfToken,
            ExpiresAt = expiresAt,
        };
    }
}
