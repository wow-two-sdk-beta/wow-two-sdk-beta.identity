using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Identity.Greetings;

namespace WoW.Two.Sdk.Beta.Be.Identity.Testing.Greetings;

/// <summary>Integrates an in-memory stand-in for the greetings capability, for a consumer's tests.</summary>
/// <param name="time">The clock greetings are stamped with; the system clock when omitted.</param>
public sealed class FakeGreetingsBroker(TimeProvider? time = null) : IGreetingsBroker
{
    private readonly List<GreetingModel> greetings = [];

    /// <summary>Gets every greeting served so far, oldest first.</summary>
    public IReadOnlyList<GreetingModel> Greetings => greetings;

    /// <inheritdoc />
    public Task<Result<GreetingModel>> GreetAsync(string? name, CancellationToken cancellationToken = default)
    {
        if (name is { Length: > GreetingConstants.MaxNameLength })
            return Task.FromResult(Result<GreetingModel>.Fail(GreetingErrors.NameTooLong()));

        var trimmed = name?.Trim();
        var greeting = new GreetingModel
        {
            Message = $"Hello, {(string.IsNullOrEmpty(trimmed) ? "world" : trimmed)}!",
            GreetedAtUtc = (time ?? TimeProvider.System).GetUtcNow(),
        };
        greetings.Add(greeting);

        return Task.FromResult(Result<GreetingModel>.Ok(greeting));
    }
}
