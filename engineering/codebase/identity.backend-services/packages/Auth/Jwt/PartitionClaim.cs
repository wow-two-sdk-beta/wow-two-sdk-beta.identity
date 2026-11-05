using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using WoW.Two.Sdk.Beta.Be.Identity.Core;

namespace WoW.Two.Sdk.Beta.Be.Identity.Jwt;

/// <summary>Keeps the tenant out of a token's own hands: only what the host trusts names it.</summary>
internal static class PartitionClaim
{
    /// <summary>Replaces whatever the token said about its tenant, in any casing, with what its issuer decides.</summary>
    /// <param name="identity">The validated identity.</param>
    /// <param name="partition">The issuer's tenant; null leaves none.</param>
    /// <param name="issuer">The issuer the claim is recorded under.</param>
    public static void Stamp(ClaimsIdentity identity, string? partition, string issuer)
    {
        var forged = identity.Claims
            .Where(claim => string.Equals(claim.Type, ServiceClaimTypeConstants.Partition, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var claim in forged)
            identity.RemoveClaim(claim);

        if (!string.IsNullOrWhiteSpace(partition))
            identity.AddClaim(new Claim(ServiceClaimTypeConstants.Partition, partition, ClaimValueTypes.String, issuer));
    }
}

/// <summary>Validates a single issuer's token as usual, then drops any tenant the token named for itself.</summary>
internal sealed class SingleIssuerTokenHandler : JsonWebTokenHandler
{
    /// <summary>Creates the handler; claims keep their token names.</summary>
    public SingleIssuerTokenHandler() => MapInboundClaims = false;

    /// <inheritdoc />
    public override async Task<TokenValidationResult> ValidateTokenAsync(string token, TokenValidationParameters validationParameters)
    {
        var result = await base.ValidateTokenAsync(token, validationParameters);
        if (result.IsValid)
            PartitionClaim.Stamp(result.ClaimsIdentity, partition: null, result.Issuer);

        return result;
    }
}
