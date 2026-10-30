using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Passwords;
using WoW.Two.Sdk.Beta.Be.Testing;

namespace Identity.Tests.E2E;

/// <summary>
/// Self sign-up as a deployed instance runs it: closed unless the environment sets <c>Identity:SignUp:Enabled</c>, the
/// boolean <c>deploy.yml</c> declares with the default <c>false</c>. Closed, <c>register</c> refuses anyone and creates
/// nothing, while an account an admin added still signs in.
/// </summary>
[Collection(IdentityApiCollection.Name)]
public sealed class SignUpTests(IdentityApiFixture fixture)
{
    private const string Password = "correct horse battery";

    [Fact]
    public async Task Register_ShouldAnswer403AndCreateNothing_WhenTheEnvironmentLeavesSignUpOff()
    {
        var email = $"closed-{Guid.NewGuid():N}@example.test";
        using var client = fixture.Host.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/account/register", new { email, password = Password });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("type").GetString().Should().Be("urn:wow-two:error:Forbidden");
        problem.RootElement.GetProperty("detail").GetString().Should().StartWith("Sign-up is closed");
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByEmailAsync(email)).Should().BeNull();
    }

    [Fact]
    public async Task SignIn_ShouldStillSucceed_ForAnAccountAnAdminAdded_WhenSignUpIsOff()
    {
        var email = $"added-{Guid.NewGuid():N}@example.test";
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var created = await scope.ServiceProvider.GetRequiredService<UserPasswordService>()
                .CreateWithPasswordAsync(new IdentityUser { UserName = email, Email = email }, Password);
            created.Succeeded.Should().BeTrue();
        }

        using var client = fixture.Host.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/account/login?useCookies=true", new { login = email, password = Password });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Register_ShouldCreateTheAccount_WhenTheEnvironmentTurnsSignUpOn()
    {
        var connectionString = await PostgresTestServer.CreateDatabaseAsync();
        try
        {
            await using var host = new WebApiTestHost<Program>
            {
                ConfigureConfigurationHook = configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DatabaseSettings:ConnectionString"] = connectionString,
                    ["AllowedHosts"] = "localhost",
                    ["Logging:LogLevel:Default"] = "Warning",
                    ["Identity:SignUp:Enabled"] = "true",
                }),
            };
            using var client = host.CreateClient();

            using var response = await client.PostAsJsonAsync("/api/account/register", new { email = "open@example.test", password = Password });

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            await using var scope = host.Services.CreateAsyncScope();
            (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByEmailAsync("open@example.test")).Should().NotBeNull();
        }
        finally
        {
            await PostgresTestServer.DropDatabaseAsync(connectionString);
        }
    }
}
