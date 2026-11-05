using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace WoW.Two.Sdk.Beta.Be.Identity.Jwt;
/// <summary>JWT bearer registration helpers.</summary>
public static class JwtServiceCollectionExtensions
{
    /// <summary>The algorithms a token between services may use: never a shared key.</summary>
    internal static readonly string[] AsymmetricAlgorithms =
    [
        SecurityAlgorithms.RsaSha256,
        SecurityAlgorithms.RsaSha384,
        SecurityAlgorithms.RsaSha512,
        SecurityAlgorithms.RsaSsaPssSha256,
        SecurityAlgorithms.RsaSsaPssSha384,
        SecurityAlgorithms.RsaSsaPssSha512,
        SecurityAlgorithms.EcdsaSha256,
        SecurityAlgorithms.EcdsaSha384,
        SecurityAlgorithms.EcdsaSha512,
    ];

    /// <summary>Register JWT bearer authentication using the supplied options.</summary>
    /// <remarks>
    /// One issuer takes <see cref="JwtOptions.Issuer"/> with one key source. <see cref="JwtOptions.TrustedIssuers"/> or a
    /// resolver turn trusted-issuer mode on: each token is checked against the issuer it names, over asymmetric keys only.
    /// </remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">Configures the JWT bearer options (issuer, audience, signing key, lifetime).</param>
    public static IServiceCollection AddJwtBearerAuthentication(this IServiceCollection services, Action<JwtOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var opts = new JwtOptions();
        configure(opts);

        if (opts.Audiences.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("JwtOptions.Audiences holds an empty audience.");

        return opts.IsTrustedIssuerMode ? AddTrustedIssuerBearer(services, opts) : AddSingleIssuerBearer(services, opts);
    }

    private static IServiceCollection AddSingleIssuerBearer(IServiceCollection services, JwtOptions opts)
    {
        if (string.IsNullOrWhiteSpace(opts.Issuer)) throw new InvalidOperationException("JwtOptions.Issuer is required.");
        if (string.IsNullOrWhiteSpace(opts.Audience)) throw new InvalidOperationException("JwtOptions.Audience is required.");
        ValidateTrustConfiguration(opts);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, b =>
            {
                b.RequireHttpsMetadata = true;
                b.SaveToken = true;
                b.MapInboundClaims = false;
                b.BackchannelHttpHandler = opts.BackchannelHttpHandler;
                b.TokenHandlers.Clear();
                b.TokenHandlers.Add(new SingleIssuerTokenHandler());
                b.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = opts.Issuer,
                    ValidateAudience = true,
                    ValidAudience = opts.Audience,
                    ValidAudiences = opts.Audiences.Count > 0 ? opts.Audiences.ToArray() : null,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = opts.IsLifetimeValidated,
                    ClockSkew = opts.ClockSkew,
                    ValidAlgorithms = [opts.Algorithm],
                    IssuerSigningKey = opts.SymmetricKey is { Length: > 0 }
                        ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.SymmetricKey))
                        : null,
                };
                if (opts.MetadataAddress is not null)
                {
                    b.MetadataAddress = opts.MetadataAddress.ToString();
                    b.RequireHttpsMetadata = !opts.IsInsecureMetadataAllowedInDevelopment;
                }
            });

        return services;
    }

    private static IServiceCollection AddTrustedIssuerBearer(IServiceCollection services, JwtOptions opts)
    {
        ValidateTrustedIssuers(opts);

        var audiences = SharedAudiencesOf(opts);
        var listed = opts.TrustedIssuers.ToArray();
        var hasResolver = opts.TrustedIssuerResolverType is not null;
        if (opts.TrustedIssuerResolverType is { } resolver)
            services.AddScoped(typeof(ITrustedIssuerResolver), resolver);

        services.AddHttpContextAccessor();
        services.AddSingleton(_ => new TrustedIssuerDiscovery(
            opts.BackchannelHttpHandler,
            requireHttps: !opts.IsInsecureMetadataAllowedInDevelopment));
        services.AddSingleton(provider => new TrustedIssuerTokenHandler(
            listed,
            audiences,
            hasResolver,
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<TrustedIssuerDiscovery>(),
            provider.GetRequiredService<IHttpContextAccessor>()));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, b =>
            {
                b.SaveToken = true;
                b.MapInboundClaims = false;
                b.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,

                    // The handler sets issuer, keys and audiences per token. These audiences are every one the host
                    // configured, kept here for a test kit that swaps the handler out and must honour them.
                    ValidAudiences = audiences
                        .Concat(listed.SelectMany(issuer => issuer.Audiences))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray(),
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = opts.IsLifetimeValidated,
                    ClockSkew = opts.ClockSkew,
                    ValidAlgorithms = AsymmetricAlgorithms,
                };
            });

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<TrustedIssuerTokenHandler>((b, handler) =>
            {
                b.TokenHandlers.Clear();
                b.TokenHandlers.Add(handler);
            });

        return services;
    }

    private static string[] SharedAudiencesOf(JwtOptions options) =>
        (string.IsNullOrWhiteSpace(options.Audience) ? options.Audiences : options.Audiences.Prepend(options.Audience))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static void ValidateTrustedIssuers(JwtOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Issuer)
            || !string.IsNullOrWhiteSpace(options.SymmetricKey)
            || options.MetadataAddress is not null)
        {
            throw new InvalidOperationException(
                "JwtOptions.Issuer, SymmetricKey and MetadataAddress describe one issuer; "
                + "with TrustedIssuers or a resolver, configure them on each TrustedIssuer.");
        }

        var everyIssuerNamesItsOwn = options.TrustedIssuers.Count > 0
            && options.TrustedIssuers.All(issuer => issuer.Audiences.Count > 0);
        if (SharedAudiencesOf(options).Length == 0 && !everyIssuerNamesItsOwn)
        {
            throw new InvalidOperationException(
                "Trusted issuers need an audience: JwtOptions.Audience or JwtOptions.Audiences, or TrustedIssuer.Audiences "
                + "on every listed issuer (a resolver alone needs one in JwtOptions).");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var issuer in options.TrustedIssuers)
        {
            ValidateTrustedIssuer(issuer, options.IsInsecureMetadataAllowedInDevelopment);
            if (!seen.Add(issuer.Issuer))
                throw new InvalidOperationException($"JwtOptions.TrustedIssuers lists '{issuer.Issuer}' twice.");
        }
    }

    /// <summary>Refuses a trusted issuer that names nothing usable, or whose keys or metadata break the trust rules.</summary>
    /// <param name="issuer">The issuer.</param>
    /// <param name="isInsecureMetadataAllowed">Whether HTTP metadata is allowed.</param>
    /// <exception cref="InvalidOperationException">The issuer is not usable.</exception>
    internal static void ValidateTrustedIssuer(TrustedIssuer issuer, bool isInsecureMetadataAllowed)
    {
        if (string.IsNullOrWhiteSpace(issuer.Issuer))
            throw new InvalidOperationException("TrustedIssuer.Issuer is required.");
        if (issuer.Audiences.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException($"Trusted issuer '{issuer.Issuer}' holds an empty audience.");

        if (issuer.SigningKeys is { } keys)
        {
            if (issuer.MetadataAddress is not null)
            {
                throw new InvalidOperationException(
                    $"Trusted issuer '{issuer.Issuer}' sets both SigningKeys and MetadataAddress; configure one.");
            }
            if (keys.Count == 0)
                throw new InvalidOperationException($"Trusted issuer '{issuer.Issuer}' has empty SigningKeys.");
            if (keys.Any(key => key is SymmetricSecurityKey or JsonWebKey { Kty: JsonWebAlgorithmsKeyTypes.Octet }))
            {
                throw new InvalidOperationException(
                    $"Trusted issuer '{issuer.Issuer}' has a symmetric key; tokens between services are signed asymmetrically.");
            }

            return;
        }

        Uri address;
        try
        {
            address = TrustedIssuerTokenHandler.MetadataAddressOf(issuer);
        }
        catch (UriFormatException exception)
        {
            throw new InvalidOperationException(
                $"Trusted issuer '{issuer.Issuer}' is not a URL; set MetadataAddress or SigningKeys.",
                exception);
        }

        if (!address.IsAbsoluteUri)
            throw new InvalidOperationException($"Trusted issuer '{issuer.Issuer}': MetadataAddress must be an absolute URI.");
        if (address.Scheme != Uri.UriSchemeHttps && !isInsecureMetadataAllowed)
        {
            throw new InvalidOperationException(
                $"Trusted issuer '{issuer.Issuer}': metadata must use HTTPS "
                + "unless IsInsecureMetadataAllowedInDevelopment is explicitly enabled.");
        }
    }

    private static void ValidateTrustConfiguration(JwtOptions options)
    {
        var hasSymmetricKey = !string.IsNullOrWhiteSpace(options.SymmetricKey);
        var hasMetadata = options.MetadataAddress is not null;
        if (hasSymmetricKey == hasMetadata)
        {
            throw new InvalidOperationException(
                "Configure exactly one JWT verification-key source: JwtOptions.SymmetricKey or JwtOptions.MetadataAddress.");
        }

        if (hasSymmetricKey)
        {
            var requiredBytes = options.Algorithm switch
            {
                SecurityAlgorithms.HmacSha256 => 32,
                SecurityAlgorithms.HmacSha384 => 48,
                SecurityAlgorithms.HmacSha512 => 64,
                _ => throw new InvalidOperationException(
                    "Symmetric JWT validation supports HS256, HS384 or HS512."),
            };

            if (Encoding.UTF8.GetByteCount(options.SymmetricKey!) < requiredBytes)
            {
                throw new InvalidOperationException(
                    $"JwtOptions.SymmetricKey must contain at least {requiredBytes} UTF-8 bytes for {options.Algorithm}.");
            }

            return;
        }

        if (!options.MetadataAddress!.IsAbsoluteUri)
            throw new InvalidOperationException("JwtOptions.MetadataAddress must be an absolute URI.");
        if (options.MetadataAddress.Scheme != Uri.UriSchemeHttps && !options.IsInsecureMetadataAllowedInDevelopment)
        {
            throw new InvalidOperationException(
                "JwtOptions.MetadataAddress must use HTTPS unless IsInsecureMetadataAllowedInDevelopment is explicitly enabled.");
        }
        if (!AsymmetricAlgorithms.Contains(options.Algorithm))
        {
            throw new InvalidOperationException(
                "Metadata-based JWT validation requires RS256/384/512, PS256/384/512 or ES256/384/512.");
        }
    }
}
