using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Identity.Jwt;

namespace WoW.Two.Sdk.Beta.Be.Identity.Signals;

/// <summary>
/// Validates <see cref="IdentitySignalReceiverOptions"/> at boot. Nothing configured passes, since a BFF with no signal URL
/// registers the receiver too; <c>MapIdentitySignals</c> refuses a route with nothing configured.
/// </summary>
internal sealed class IdentitySignalReceiverOptionsValidator : IValidateOptions<IdentitySignalReceiverOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, IdentitySignalReceiverOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var audiences = string.IsNullOrWhiteSpace(options.Audience) ? options.Audiences : [.. options.Audiences, options.Audience];
        if (audiences.Count == 0 && options.TrustedIssuers.Count == 0)
            return ValidateOptionsResult.Success;

        var failures = new List<string>();
        if (audiences.Count == 0)
            failures.Add($"{nameof(IdentitySignalReceiverOptions)}.{nameof(IdentitySignalReceiverOptions.Audience)} is required: this service's own signal URL.");
        if (options.TrustedIssuers.Count == 0)
            failures.Add($"{nameof(IdentitySignalReceiverOptions)}.{nameof(IdentitySignalReceiverOptions.TrustedIssuers)} needs an issuer.");

        foreach (var audience in audiences)
        {
            // On Unix a bare "/path" parses as an absolute file URI.
            if (!Uri.TryCreate(audience, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
                failures.Add($"{nameof(IdentitySignalReceiverOptions)}: the signal URL '{audience}' must be an absolute HTTP or HTTPS URL.");
        }

        foreach (var issuer in options.TrustedIssuers)
        {
            try
            {
                JwtServiceCollectionExtensions.ValidateTrustedIssuer(issuer, options.IsInsecureMetadataAllowedInDevelopment);
            }
            catch (InvalidOperationException exception)
            {
                failures.Add(exception.Message);
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
