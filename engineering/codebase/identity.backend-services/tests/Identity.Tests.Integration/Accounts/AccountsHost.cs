using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Identity.Core;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests.Accounts;

/// <summary>An identity registration over in-memory SQLite, its schema from the Module's migrations, with a controllable clock.</summary>
internal sealed class AccountsHost : IAsyncDisposable
{
    public const string SigningKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    private readonly IdentityTestDatabase _database;

    private AccountsHost(IdentityTestDatabase database, ServiceProvider provider, FakeTimeProvider time)
    {
        _database = database;
        Provider = provider;
        Time = time;
    }

    public ServiceProvider Provider { get; }

    public FakeTimeProvider Time { get; }

    public static AccountsHost Create(
        Action<IdentityBuilder> slices,
        Action<IdentityCoreOptions>? options = null,
        Action<IServiceCollection>? services = null)
    {
        var database = new IdentityTestDatabase();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 28, 9, 0, 0, TimeSpan.Zero));

        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddSingleton<TimeProvider>(time);
        database.Register(collection);
        slices(collection.AddUserAccounts(options, IdentityTestDatabase.UseSqlite));
        services?.Invoke(collection);

        var provider = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        Task.Run(() => IdentityTestDatabase.MigrateAsync(provider)).GetAwaiter().GetResult();

        return new AccountsHost(database, provider, time);
    }

    public AsyncServiceScope Scope() => Provider.CreateAsyncScope();

    public async ValueTask DisposeAsync()
    {
        await Provider.DisposeAsync();
        await _database.DisposeAsync();
    }

    /// <summary>Creates and persists a user through the core service.</summary>
    public async Task<IdentityUser> CreateUserAsync(string userName, string? email = null)
    {
        await using var scope = Scope();
        var user = new IdentityUser { UserName = userName, Email = email ?? $"{userName}@example.test" };
        var result = await scope.ServiceProvider.GetRequiredService<UserAccountService>().CreateAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException(result.Errors[0].Description);

        return user;
    }

    /// <summary>Reloads a user in a fresh scope, as the next request would see it.</summary>
    public async Task<IdentityUser> ReloadAsync(Guid id)
    {
        await using var scope = Scope();
        return await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(id)
            ?? throw new InvalidOperationException($"User {id} is missing.");
    }
}
