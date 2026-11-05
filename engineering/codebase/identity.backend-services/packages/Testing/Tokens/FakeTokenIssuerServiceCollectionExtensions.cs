using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace WoW.Two.Sdk.Beta.Be.Identity.Testing;

/// <summary>Registers a <see cref="FakeTokenIssuer"/> on a host under test.</summary>
public static class FakeTokenIssuerServiceCollectionExtensions
{
    /// <summary>
    /// Makes the default <c>JwtBearer</c> scheme trust only <paramref name="issuer"/>, with no discovery and no network: a
    /// host's metadata address, trusted issuers and resolver stop being read. The audiences the host configured stay,
    /// so a test still proves them.
    /// </summary>
    /// <remarks>
    /// A trusted issuer's <c>Partition</c> leaves with its handler, so no <c>partition</c> is stamped. For a test that needs
    /// one, add <see cref="FakeTokenIssuer.AsTrustedIssuer"/> to the host's trusted issuers instead.
    /// </remarks>
    /// <param name="services">The host's services.</param>
    /// <param name="issuer">The fake to trust; the test keeps owning it.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddFakeTokenIssuer(this IServiceCollection services, FakeTokenIssuer issuer)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(issuer);

        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Authority = null;
            options.MetadataAddress = null!;
            options.ConfigurationManager = null;
            options.Configuration = null;
            options.UseSecurityTokenValidators = false;
            options.TokenHandlers.Clear();
            options.TokenHandlers.Add(new JsonWebTokenHandler { MapInboundClaims = false });

            var parameters = options.TokenValidationParameters;
            parameters.ConfigurationManager = null;
            parameters.ValidIssuer = issuer.Issuer;
            parameters.ValidIssuers = null;
            parameters.IssuerValidator = null;
            parameters.IssuerSigningKey = issuer.VerificationKey;
            parameters.IssuerSigningKeys = null;
            parameters.IssuerSigningKeyResolver = null;
            parameters.ValidAlgorithms = [SecurityAlgorithms.RsaSha256];
        });
        return services;
    }
}
