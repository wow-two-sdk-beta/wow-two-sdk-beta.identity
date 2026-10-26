using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Identity.Greetings;

/// <summary>Integrates the Identity greetings capability, in process or over the running service.</summary>
/// <remarks>Every form implements this one contract, and one conformance suite runs against each implementation.</remarks>
public interface IGreetingsBroker
{
    /// <summary>Greets <paramref name="name"/>, or the world when the name is blank.</summary>
    /// <param name="name">The name to greet; surrounding whitespace is ignored.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The greeting; a validation failure when the name exceeds <see cref="GreetingConstants.MaxNameLength"/>.</returns>
    Task<Result<GreetingModel>> GreetAsync(string? name, CancellationToken cancellationToken = default);
}
