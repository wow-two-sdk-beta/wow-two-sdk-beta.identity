using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration;

namespace WoW.Two.Sdk.Beta.Be.Identity.Core.TestMode;

/// <summary>Refuses test mode on a deployed boot, failing closed: with no environment to judge by, the boot counts as deployed.</summary>
/// <param name="services">The root provider, to tell a local run from a deployed boot.</param>
internal sealed class TestModeOptionsValidator(IServiceProvider services) : IValidateOptions<TestModeOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TestModeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.IsEnabled || IsLocalRun())
            return ValidateOptionsResult.Success;

        return ValidateOptionsResult.Fail(
            $"{TestModeOptions.SectionName}:{nameof(options.IsEnabled)} is allowed only on a local run (Development without DEPLOY_ENVIRONMENT); test mode hands every code the instance sends to any caller.");
    }

    private bool IsLocalRun()
        => services.GetService<IHostEnvironment>() is { } environment
            && services.GetService<IConfiguration>() is { } configuration
            && environment.IsLocalRun(configuration);
}
