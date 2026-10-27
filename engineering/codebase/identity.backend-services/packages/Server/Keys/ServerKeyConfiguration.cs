using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration;
using WoW.Two.Sdk.Beta.Be.Identity.OidcServer;

namespace WoW.Two.Sdk.Beta.Be.Identity.Keys;

/// <summary>
/// Hands the server its keys when its options are built at boot, failing closed: certificates from
/// <see cref="ISigningKeySource"/> that pass <see cref="SigningKeyValidator"/>, or throwaway RSA keys when
/// <see cref="ServerKeyOptions.Ephemeral"/> is set on a local run.
/// </summary>
/// <param name="services">The root provider.</param>
internal sealed partial class ServerKeyConfiguration(IServiceProvider services) : IConfigureOptions<OpenIddictServerOptions>
{
    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Ephemeral keys outside a local run, ephemeral keys beside files, or unusable certificates.</exception>
    public void Configure(OpenIddictServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var keys = services.GetRequiredService<OidcServerOptions>().Keys;
        if (keys.Ephemeral)
        {
            if (!IsLocalRun())
                throw new InvalidOperationException("Identity:Server:Keys:Ephemeral is allowed only on a local run (Development without DEPLOY_ENVIRONMENT); a deployed boot reads certificate files.");
            if (keys.Signing.Count > 0 || keys.Encryption.Count > 0)
                throw new InvalidOperationException("Identity:Server:Keys sets Ephemeral beside certificate files; choose one.");

            options.SigningCredentials.Add(new SigningCredentials(CreateEphemeralKey(), SecurityAlgorithms.RsaSha256));
            options.EncryptionCredentials.Add(new EncryptingCredentials(CreateEphemeralKey(), SecurityAlgorithms.RsaOAEP, SecurityAlgorithms.Aes256CbcHmacSha512));
            return;
        }

        var set = services.GetRequiredService<ISigningKeySource>().Load();
        var now = (services.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
        var problems = SigningKeyValidator.Validate(set, now);
        if (problems.Count > 0)
            throw new InvalidOperationException($"The OIDC server's keys are not usable: {string.Join(" ", problems)}");

        var logger = services.GetService<ILoggerFactory>()?.CreateLogger<ServerKeyConfiguration>();
        foreach (var certificate in set.Signing)
            options.SigningCredentials.Add(new SigningCredentials(new X509SecurityKey(certificate), SecurityAlgorithms.RsaSha256));
        foreach (var certificate in set.Encryption)
            options.EncryptionCredentials.Add(new EncryptingCredentials(new X509SecurityKey(certificate), SecurityAlgorithms.RsaOAEP, SecurityAlgorithms.Aes256CbcHmacSha512));

        if (logger is not null)
        {
            foreach (var role in Enum.GetValues<SigningKeyRole>())
                LogActive(logger, role, SigningKeyValidator.Describe(SigningKeySet.SelectActive(set.Of(role), now)!), set.Of(role).Count);
        }
    }

    private bool IsLocalRun()
        => services.GetService<IHostEnvironment>() is { } environment
            && services.GetService<IConfiguration>() is { } configuration
            && environment.IsLocalRun(configuration);

    private static RsaSecurityKey CreateEphemeralKey()
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa.ExportParameters(includePrivateParameters: true));
        key.KeyId = Base64UrlEncoder.Encode(key.ComputeJwkThumbprint());
        return key;
    }

    [LoggerMessage(EventId = 7101, Level = LogLevel.Information, Message = "OIDC {Role} certificate active: {Certificate}, of {Count} configured")]
    private static partial void LogActive(ILogger logger, SigningKeyRole role, string certificate, int count);
}
