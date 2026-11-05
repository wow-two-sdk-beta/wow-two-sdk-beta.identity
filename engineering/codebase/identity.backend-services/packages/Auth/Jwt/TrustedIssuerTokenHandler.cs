using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace WoW.Two.Sdk.Beta.Be.Identity.Jwt;

/// <summary>
/// Validates a token against the issuer it names: that issuer's keys, its audiences or the shared ones, and nothing else,
/// so one issuer's keys never validate another's tokens. Its metadata is fetched only once the issuer is trusted, and an
/// unknown issuer gets an invalid result without any network call.
/// </summary>
/// <remarks>
/// A failure in the resolver or in discovery surfaces as an exception, which the bearer handler records as a failed
/// authentication: the caller gets a <c>401</c> and the host's log gets the reason.
/// </remarks>
internal sealed class TrustedIssuerTokenHandler : JsonWebTokenHandler
{
    private readonly Dictionary<string, TrustedIssuer> _listed;
    private readonly IReadOnlyList<string> _audiences;
    private readonly bool _hasResolver;
    private readonly IServiceScopeFactory _scopes;
    private readonly TrustedIssuerDiscovery _discovery;
    private readonly IHttpContextAccessor _httpContext;

    /// <param name="listed">The issuers known at boot; the boot refused duplicates.</param>
    /// <param name="audiences">The audiences of an issuer that names none.</param>
    /// <param name="hasResolver">Whether <see cref="ITrustedIssuerResolver"/> is registered for issuers not listed.</param>
    /// <param name="scopes">Opens the scope each resolver call runs in.</param>
    /// <param name="discovery">The discovery cache.</param>
    /// <param name="httpContext">Supplies the request's abort signal, which the handler signature leaves out.</param>
    public TrustedIssuerTokenHandler(
        IEnumerable<TrustedIssuer> listed,
        IReadOnlyList<string> audiences,
        bool hasResolver,
        IServiceScopeFactory scopes,
        TrustedIssuerDiscovery discovery,
        IHttpContextAccessor httpContext)
    {
        _listed = listed.ToDictionary(issuer => issuer.Issuer, StringComparer.Ordinal);
        _audiences = audiences;
        _hasResolver = hasResolver;
        _scopes = scopes;
        _discovery = discovery;
        _httpContext = httpContext;
        MapInboundClaims = false;
    }

    /// <inheritdoc />
    public override async Task<TokenValidationResult> ValidateTokenAsync(
        string token,
        TokenValidationParameters validationParameters)
    {
        ArgumentNullException.ThrowIfNull(validationParameters);

        var name = ReadIssuer(token);
        if (string.IsNullOrEmpty(name))
            return Refuse(new SecurityTokenMalformedException("The token names no readable issuer."));

        var cancellationToken = _httpContext.HttpContext?.RequestAborted ?? CancellationToken.None;

        // InvalidIssuer stays unset: the bearer handler quotes it in WWW-Authenticate, and this value is unverified.
        if (await FindIssuerAsync(name, cancellationToken) is not { } issuer)
            return Refuse(new SecurityTokenInvalidIssuerException("The token's issuer is not trusted."));

        IEnumerable<SecurityKey> keys = issuer.SigningKeys is { } own
            ? own
            : await _discovery.GetKeysAsync(MetadataAddressOf(issuer), cancellationToken);
        var result = await base.ValidateTokenAsync(token, ParametersFor(validationParameters, issuer, keys));

        if (issuer.SigningKeys is null && result.Exception is SecurityTokenSignatureKeyNotFoundException)
        {
            var fresh = await _discovery.RefreshKeysAsync(MetadataAddressOf(issuer), cancellationToken);
            if (!ReferenceEquals(fresh, keys))
                result = await base.ValidateTokenAsync(token, ParametersFor(validationParameters, issuer, fresh));
        }

        if (result.IsValid)
            PartitionClaim.Stamp(result.ClaimsIdentity, issuer.Partition, issuer.Issuer);

        return result;
    }

    /// <summary>The issuer's discovery address: its own, or the well-known one under its name.</summary>
    /// <param name="issuer">The issuer.</param>
    internal static Uri MetadataAddressOf(TrustedIssuer issuer) =>
        issuer.MetadataAddress ?? new Uri($"{issuer.Issuer.TrimEnd('/')}/.well-known/openid-configuration");

    private string? ReadIssuer(string token)
    {
        if (!CanReadToken(token))
            return null;

        try
        {
            return ReadJsonWebToken(token).Issuer;
        }
        catch (Exception exception) when (exception is ArgumentException or SecurityTokenException)
        {
            return null;
        }
    }

    private async ValueTask<TrustedIssuer?> FindIssuerAsync(string name, CancellationToken cancellationToken)
    {
        if (_listed.TryGetValue(name, out var listed))
            return listed;
        if (!_hasResolver)
            return null;

        await using var scope = _scopes.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ITrustedIssuerResolver>();
        var resolved = await resolver.ResolveAsync(name, cancellationToken);
        return resolved is not null && string.Equals(resolved.Issuer, name, StringComparison.Ordinal) ? resolved : null;
    }

    private TokenValidationParameters ParametersFor(
        TokenValidationParameters template,
        TrustedIssuer issuer,
        IEnumerable<SecurityKey> keys)
    {
        var parameters = template.Clone();
        parameters.ValidIssuer = issuer.Issuer;
        parameters.ValidIssuers = null;
        parameters.IssuerSigningKey = null;
        parameters.IssuerSigningKeys = keys;
        parameters.ValidAudience = null;
        parameters.ValidAudiences = issuer.Audiences.Count > 0 ? issuer.Audiences : _audiences;
        return parameters;
    }

    private static TokenValidationResult Refuse(Exception exception) => new() { IsValid = false, Exception = exception };
}
