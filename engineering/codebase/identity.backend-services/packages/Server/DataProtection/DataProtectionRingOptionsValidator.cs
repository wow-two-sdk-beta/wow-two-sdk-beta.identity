using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration;

namespace WoW.Two.Sdk.Beta.Be.Identity.DataProtection;

/// <summary>
/// Checks the ring at boot, failing closed: a named application, readable certificates with RSA private keys of 2048
/// bits or more and the key-encipherment usage, one valid now, and at least one on a deployed boot.
/// </summary>
/// <param name="services">The root provider, to load the certificates and tell a local run from a deployed boot.</param>
internal sealed class DataProtectionRingOptionsValidator(IServiceProvider services) : IValidateOptions<DataProtectionRingOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, DataProtectionRingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ApplicationName))
            problems.Add($"{DataProtectionRingOptions.SectionName}:{nameof(options.ApplicationName)} is required.");

        // Loaded here from the options being checked, never through the runtime set, which itself waits on these options.
        var (certificates, loadProblems) = DataProtectionRingCertificates.Load(
            options, (services.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow(), services.GetService<IHostEnvironment>());
        foreach (var certificate in certificates)
            certificate.Dispose();

        problems.AddRange(loadProblems);
        if (certificates.Count == 0 && loadProblems.Count == 0 && !IsLocalRun())
        {
            problems.Add(
                $"{DataProtectionRingOptions.SectionName}:{nameof(options.Certificates)} lists no certificate; a deployed boot wraps the ring at rest, " +
                "so set :0 to a bundle from 'keys generate --role data-protection'.");
        }

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }

    private bool IsLocalRun()
        => services.GetService<IHostEnvironment>() is { } environment
            && services.GetService<IConfiguration>() is { } configuration
            && environment.IsLocalRun(configuration);
}
