using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Identity.Greetings;
using WoW.Two.Sdk.Beta.Be.Identity.Module.Persistence;

namespace WoW.Two.Sdk.Beta.Be.Identity.Module.Greetings;

/// <summary>Integrates the in-process greetings module: composes the greeting and logs it to the module's store.</summary>
internal sealed class ModuleGreetingsBroker(IdentityDbContext db, TimeProvider time) : IGreetingsBroker
{
    /// <inheritdoc />
    public async Task<Result<GreetingModel>> GreetAsync(string? name, CancellationToken cancellationToken = default)
    {
        if (name is { Length: > GreetingConstants.MaxNameLength })
            return Result<GreetingModel>.Fail(GreetingErrors.NameTooLong());

        var greeting = GreetingComposer.Compose(name, time.GetUtcNow());
        db.GreetingLogs.Add(new GreetingLog { Id = Guid.CreateVersion7(), Message = greeting.Message, GreetedAtUtc = greeting.GreetedAtUtc });
        await db.SaveChangesAsync(cancellationToken);

        return Result<GreetingModel>.Ok(greeting);
    }
}
