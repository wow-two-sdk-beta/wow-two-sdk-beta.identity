using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Identity.Core;

namespace WoW.Two.Sdk.Beta.Be.Identity.Tests;

/// <summary>Identity core vertical — create → find → delete a user, and case-insensitive duplicate rejection, over PostgreSQL.</summary>
public sealed class UserAccountServiceTests
{
    private static async Task<ServiceProvider> BuildProviderAsync(IdentityTestDatabase database)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        database.Register(services);
        services.AddUserAccounts(o => o.User.RequireUniqueEmail = true);

        var provider = services.BuildServiceProvider();
        await IdentityTestDatabase.MigrateAsync(provider);
        return provider;
    }

    [Fact]
    public async Task Creates_finds_and_deletes_a_user()
    {
        await using var database = new IdentityTestDatabase();
        await using var provider = await BuildProviderAsync(database);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<UserAccountService>();

        var user = new IdentityUser { UserName = "alice", Email = "alice@example.test" };
        (await service.CreateAsync(user)).Succeeded.Should().BeTrue();
        user.Id.Version.Should().Be(7);                // a uuid v7, assigned at creation
        user.NormalizedUserName.Should().Be("ALICE");
        user.SecurityStamp.Should().NotBeNullOrEmpty();

        (await service.FindByIdAsync(user.Id)).Should().NotBeNull();
        (await service.FindByNameAsync("ALICE")).Should().NotBeNull();               // case-insensitive
        (await service.FindByEmailAsync("ALICE@EXAMPLE.TEST")).Should().NotBeNull();

        (await service.DeleteAsync(user)).Succeeded.Should().BeTrue();
        (await service.FindByIdAsync(user.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Rejects_duplicate_user_name_case_insensitively()
    {
        await using var database = new IdentityTestDatabase();
        await using var provider = await BuildProviderAsync(database);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<UserAccountService>();

        (await service.CreateAsync(new IdentityUser { UserName = "bob", Email = "bob@x.test" })).Succeeded.Should().BeTrue();
        var duplicate = await service.CreateAsync(new IdentityUser { UserName = "BOB", Email = "bob2@x.test" });

        duplicate.Succeeded.Should().BeFalse();
        duplicate.Errors.Should().Contain(e => e.Code == "DuplicateUserName");
    }

    [Fact]
    public async Task Repository_rejects_a_replacement_while_the_original_user_is_tracked()
    {
        await using var database = new IdentityTestDatabase();
        await using var provider = await BuildProviderAsync(database);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<UserAccountService>();
        var repository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var original = new IdentityUser { UserName = "tracked", Email = "tracked@example.test" };
        (await service.CreateAsync(original)).Succeeded.Should().BeTrue();
        var replacement = new IdentityUser
        {
            Id = original.Id,
            UserName = original.UserName,
            NormalizedUserName = original.NormalizedUserName,
            Email = "changed@example.test",
            NormalizedEmail = "CHANGED@EXAMPLE.TEST",
            SecurityStamp = original.SecurityStamp,
            ConcurrencyStamp = original.ConcurrencyStamp,
        };

        var act = () => repository.UpdateAsync(replacement);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Apply accepted changes to the tracked instance*");
    }
}
