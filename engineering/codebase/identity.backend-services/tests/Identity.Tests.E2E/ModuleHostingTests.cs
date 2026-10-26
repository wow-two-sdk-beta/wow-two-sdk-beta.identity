using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Identity.Core;

namespace Identity.Tests.E2E;

/// <summary>The service hosts the Form 1 module over its own database: an account created through the module's facade reads back in a fresh scope.</summary>
[Collection(IdentityApiCollection.Name)]
public sealed class ModuleHostingTests(IdentityApiFixture fixture)
{
    [Fact]
    public async Task UserAccounts_ShouldStoreTheUser_InTheServiceDatabase()
    {
        var userName = $"e2e-{Guid.NewGuid():N}";
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var created = await scope.ServiceProvider.GetRequiredService<UserAccountService<IdentityUser, Guid>>()
                .CreateAsync(new IdentityUser { UserName = userName, Email = $"{userName}@example.test" });

            created.Succeeded.Should().BeTrue();
        }

        await using var next = fixture.Host.Services.CreateAsyncScope();
        var found = await next.ServiceProvider.GetRequiredService<UserAccountService<IdentityUser, Guid>>().FindByNameAsync(userName);

        found.Should().NotBeNull();
        found!.Email.Should().Be($"{userName}@example.test");
    }
}
