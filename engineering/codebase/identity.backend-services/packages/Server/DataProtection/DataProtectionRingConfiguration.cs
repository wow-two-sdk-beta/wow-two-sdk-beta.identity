using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace WoW.Two.Sdk.Beta.Be.Identity.DataProtection;

/// <summary>
/// Points the key manager at the database ring and, when a ring certificate is listed, wraps every new key to the active
/// one. Built when the key ring is first used; unusable certificates refuse it, as the boot check does.
/// </summary>
/// <param name="services">The root provider.</param>
internal sealed partial class DataProtectionRingConfiguration(IServiceProvider services) : IConfigureOptions<KeyManagementOptions>
{
    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The ring certificates are unusable.</exception>
    public void Configure(KeyManagementOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var loggers = services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;
        options.XmlRepository = new DatabaseXmlRepository(services, loggers.CreateLogger<DatabaseXmlRepository>());

        var certificates = services.GetRequiredService<DataProtectionRingCertificates>();
        if (certificates.Problems.Count > 0)
            throw new InvalidOperationException($"The Data Protection ring certificates are not usable: {string.Join(" ", certificates.Problems)}");

        var logger = loggers.CreateLogger<DataProtectionRingConfiguration>();
        if (certificates.Active is { } active)
        {
            options.XmlEncryptor = new CertificateRingXmlEncryptor(active, loggers);
            LogWrapped(logger, Keys.SigningKeyValidator.Describe(active), certificates.All.Count);
        }
        else
        {
            LogUnwrapped(logger);
        }
    }

    [LoggerMessage(EventId = 7202, Level = LogLevel.Information, Message = "Data Protection ring wrapped by certificate {Certificate}, of {Count} configured")]
    private static partial void LogWrapped(ILogger logger, string certificate, int count);

    [LoggerMessage(EventId = 7203, Level = LogLevel.Warning, Message = "Data Protection ring stored unwrapped: no ring certificate is listed, which only a local run allows")]
    private static partial void LogUnwrapped(ILogger logger);
}
