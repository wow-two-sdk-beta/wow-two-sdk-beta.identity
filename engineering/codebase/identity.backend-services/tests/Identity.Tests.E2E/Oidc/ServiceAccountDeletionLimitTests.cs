using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Identity.Api.AccountDeletion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using WoW.Two.Sdk.Beta.Be.Identity.Core;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Roles;
using WoW.Two.Sdk.Beta.Be.Identity.Core.SignInLog;

namespace Identity.Tests.E2E.Oidc;

/// <summary>
/// The deletion route is limited to the caller's product and audited, through the real signer and service credentials.
/// Two products share the instance: the first owns the test client (<c>e2e-app</c>) and deletes through
/// <c>e2e-first-deleter</c>; the second owns <c>e2e-second-app</c> and deletes through <c>e2e-second-deleter</c>. A deletion
/// client reaches a person only through its product: <c>404</c> for one who never authorized it, <c>409</c> for one holding an
/// organization or console role, <c>200</c> <c>{ "erased": false }</c> (its authorizations and tokens revoked, the person kept)
/// for one who also uses the other product, <c>204</c> otherwise. Every call writes a sign-in log row.
/// </summary>
public sealed class ServiceAccountDeletionLimitTests(ServiceAccountDeletionLimitTests.Fixture fixture) : IClassFixture<ServiceAccountDeletionLimitTests.Fixture>
{
    private const string Audience = "https://localhost/api/service/people";
    private const string DeleteScope = "accounts.delete";
    private const string FirstDeleter = "e2e-first-deleter";
    private const string SecondDeleter = "e2e-second-deleter";
    private const string SecondApp = "e2e-second-app";
    private const string Stranger = "e2e-unlisted-deleter";
    private const string Secret = "e2e-test-only-account-service-secret";
    private const string Password = "e2e-correct-horse-battery";

    private OidcHost Server => fixture.Server;

    [Fact]
    public async Task PersonWhoNeverAuthorizedTheProduct_ShouldAnswerNotFound_AndStayUntouched()
    {
        using var browser = Server.CreateClient();
        var email = Email("nobody");
        var person = await browser.SignUpAndSignInAsync(email, Password);
        var other = Guid.NewGuid();

        using var known = await DeleteAsync(FirstDeleter, person);
        using var unknown = await DeleteAsync(FirstDeleter, other);

        foreach (var response in new[] { known, unknown })
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await CodeOfAsync(response)).Should().Be("PersonNotFound");
        }

        using var again = Server.CreateClient();
        await again.SignInAsync(email, Password);
        (await AuditOfAsync(person)).Should().Equal(new Audit(FirstDeleter, "not_found", "not_product_person"));
        (await AuditOfAsync(other)).Should().Equal(new Audit(FirstDeleter, "not_found", "not_product_person"));
    }

    [Theory]
    [InlineData("organization", "organization_role")]
    [InlineData("console", "console_permission")]
    public async Task PersonHoldingARole_ShouldAnswerConflict_AndKeepEverything(string role, string reason)
    {
        using var browser = Server.CreateClient();
        var person = await browser.SignUpAndSignInAsync(Email(role), Password);
        var tokens = await browser.SignInToAppAsync();
        if (role == "organization")
            await JoinOrganizationAsync(person);
        else
            await GrantConsoleRoleAsync(person);

        using var response = await DeleteAsync(FirstDeleter, person);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeOfAsync(response)).Should().Be("PersonHoldsAccess");
        using var refreshed = await browser.RefreshAsync(tokens.RefreshToken);
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK, "nothing was revoked");
        (await AuditOfAsync(person)).Should().Equal(new Audit(FirstDeleter, "blocked", reason));
    }

    [Fact]
    public async Task PersonOfTwoProducts_ShouldLoseOnlyTheCallersProduct_AndStay()
    {
        using var browser = Server.CreateClient();
        var email = Email("both");
        var person = await browser.SignUpAndSignInAsync(email, Password);
        var tokens = await browser.SignInToAppAsync();
        await AuthorizeSecondAppAsync(person);

        using var first = await DeleteAsync(FirstDeleter, person);
        using var retry = await DeleteAsync(FirstDeleter, person);

        foreach (var response in new[] { first, retry })
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Be("""{"erased":false}""");
        }

        using var refreshed = await browser.RefreshAsync(tokens.RefreshToken);
        refreshed.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the product's tokens are revoked");
        (await refreshed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Be("invalid_grant");
        using var signedIn = Server.CreateClient();
        await signedIn.SignInAsync(email, Password);
        (await StatusesAsync(person)).Should().BeEquivalentTo(new Dictionary<string, string> { [OidcHost.ClientId] = "revoked", [SecondApp] = "valid" }, "the other product keeps its grant");
        (await AuditOfAsync(person)).Should().Equal(
            new Audit(FirstDeleter, "kept", "other_products"),
            new Audit(FirstDeleter, "kept", "other_products"));
    }

    [Fact]
    public async Task PersonOfOnlyTheProduct_ShouldBeErased_AndEveryCallAudited()
    {
        using var browser = Server.CreateClient();
        var email = Email("only");
        var person = await browser.SignUpAndSignInAsync(email, Password);
        var tokens = await browser.SignInToAppAsync();

        using var first = await DeleteAsync(FirstDeleter, person);
        using var retry = await DeleteAsync(FirstDeleter, person);

        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        retry.StatusCode.Should().Be(HttpStatusCode.NoContent, "a retry after a lost response meets the same answer");
        using var refreshed = await browser.RefreshAsync(tokens.RefreshToken);
        refreshed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var newlyRegistered = Server.CreateClient();
        (await newlyRegistered.SignUpAndSignInAsync(email, Password)).Should().NotBe(person, "the erased address is free again");

        var audit = await AuditRowsAsync(person);
        audit.Select(row => (row.Method, row.ClientId, row.Result, row.Reason)).Should().Equal(
            (ServiceDeletionAuditConstants.Method, FirstDeleter, "erased", null),
            (ServiceDeletionAuditConstants.Method, FirstDeleter, "erased", null));
        audit.Should().AllSatisfy(row => row.OccurredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public async Task SecondProductsDeletionClient_ShouldNotTouchTheFirstProductsPerson()
    {
        using var browser = Server.CreateClient();
        var email = Email("first-product");
        var person = await browser.SignUpAndSignInAsync(email, Password);
        var tokens = await browser.SignInToAppAsync();

        using var refused = await DeleteAsync(SecondDeleter, person);

        refused.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeOfAsync(refused)).Should().Be("PersonNotFound");
        using var refreshed = await browser.RefreshAsync(tokens.RefreshToken);
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK, "the other product's deletion client erased nothing");
        using var signedIn = Server.CreateClient();
        await signedIn.SignInAsync(email, Password);
        (await AuditOfAsync(person)).Should().Equal(new Audit(SecondDeleter, "not_found", "not_product_person"));

        // The same client erases its own product's person, and the first product's client cannot reach that one.
        var neighbor = await AuthorizedOnlyBySecondAppAsync(Email("second-product"));
        using var notFirsts = await DeleteAsync(FirstDeleter, neighbor);
        using var theirs = await DeleteAsync(SecondDeleter, neighbor);
        notFirsts.StatusCode.Should().Be(HttpStatusCode.NotFound);
        theirs.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await AuditOfAsync(neighbor)).Should().Equal(new Audit(FirstDeleter, "not_found", "not_product_person"), new Audit(SecondDeleter, "erased", null));
    }

    [Fact]
    public async Task ClientNotListedInTheSettings_ShouldBeRefused_EvenWithTheScope()
    {
        using var browser = Server.CreateClient();
        var person = await browser.SignUpAndSignInAsync(Email("unlisted"), Password);
        await browser.SignInToAppAsync();

        using var response = await DeleteAsync(Stranger, person);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AuditOfAsync(person)).Should().BeEmpty("a refused caller reaches no audit: it never gets to the route");
    }

    [Theory]
    [InlineData("audience-without-clients")]
    [InlineData("clients-without-audience")]
    [InlineData("client-with-blank-product")]
    public async Task UnrestrictedOrHalfConfiguredDeletion_ShouldRefuseToBoot(string shape)
    {
        var settings = new Dictionary<string, string?> { ["Identity:Server:Issuer"] = "https://localhost/" };
        if (shape != "clients-without-audience")
            settings["Identity:AccountDeletion:Audience"] = Audience;
        if (shape != "audience-without-clients")
            settings[$"Identity:AccountDeletion:Clients:{FirstDeleter}:ProductClientIds:0"] = shape == "client-with-blank-product" ? " " : OidcHost.ClientId;
        await using var host = new OidcHost(settings);

        FluentActions.Invoking(() => host.Host.CreateClient()).Should().Throw<Exception>().Which.ToString().Should().Contain("AccountDeletion");
    }

    private async Task<HttpResponseMessage> DeleteAsync(string deleter, Guid person)
    {
        using var service = Server.CreateClient();
        var http = service.Http;
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = deleter,
            ["client_secret"] = Secret,
            ["resource"] = Audience,
            ["scope"] = DeleteScope,
        });
        using var token = await http.PostAsync("/connect/token", body);
        token.EnsureSuccessStatusCode();
        var access = (await token.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString()!;
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/service/people/{person:D}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        return await http.SendAsync(request);
    }

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("identityCode").GetString();
    }

    private static string Email(string name) => $"{name}-{Guid.NewGuid():N}@example.test";

    private async Task JoinOrganizationAsync(Guid personId)
    {
        await using var scope = Server.Host.Services.CreateAsyncScope();
        var person = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(personId))!;
        var organization = new IdentityOrganization { Name = "Held", Slug = $"held-{Guid.NewGuid():N}"[..20] };
        (await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateAsync(organization, person)).IsSuccess.Should().BeTrue();
    }

    private async Task GrantConsoleRoleAsync(Guid personId)
    {
        await using var scope = Server.Host.Services.CreateAsyncScope();
        var person = (await scope.ServiceProvider.GetRequiredService<UserAccountService>().FindByIdAsync(personId))!;
        (await scope.ServiceProvider.GetRequiredService<InstanceRoleService>().GrantAsync(person, InstanceRoleConstants.Support)).IsGranted.Should().BeTrue();
    }

    /// <summary>Gives the person a grant for the second product's client, as that product's sign-in would.</summary>
    private async Task AuthorizeSecondAppAsync(Guid person)
    {
        await using var scope = Server.Host.Services.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var application = (await applications.FindByClientIdAsync(SecondApp))!;
        var descriptor = new OpenIddictAuthorizationDescriptor
        {
            ApplicationId = await applications.GetIdAsync(application),
            CreationDate = DateTimeOffset.UtcNow,
            Status = OpenIddictConstants.Statuses.Valid,
            Subject = person.ToString("D"),
            Type = OpenIddictConstants.AuthorizationTypes.AdHoc,
        };
        descriptor.Scopes.Add(OpenIddictConstants.Scopes.OpenId);
        await scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>().CreateAsync(descriptor);
    }

    private async Task<Guid> AuthorizedOnlyBySecondAppAsync(string email)
    {
        using var browser = Server.CreateClient();
        var person = await browser.SignUpAndSignInAsync(email, Password);
        await AuthorizeSecondAppAsync(person);
        return person;
    }

    /// <summary>The status of each client's authorization of the person.</summary>
    private async Task<Dictionary<string, string>> StatusesAsync(Guid person)
    {
        await using var scope = Server.Host.Services.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var found = new List<object>();
        await foreach (var authorization in authorizations.FindBySubjectAsync(person.ToString("D")))
            found.Add(authorization);

        var statuses = new Dictionary<string, string>();
        foreach (var authorization in found)
        {
            var application = (await applications.FindByIdAsync((await authorizations.GetApplicationIdAsync(authorization))!))!;
            statuses[(await applications.GetClientIdAsync(application))!] = (await authorizations.GetStatusAsync(authorization))!;
        }

        return statuses;
    }

    private async Task<List<IdentitySignInLogEntry>> AuditRowsAsync(Guid person)
    {
        await using var scope = Server.Host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().SignInLog.AsNoTracking()
            .Where(row => row.UserId == person && row.Method == ServiceDeletionAuditConstants.Method)
            .OrderBy(row => row.Id)
            .ToListAsync();
    }

    private async Task<List<Audit>> AuditOfAsync(Guid person)
        => (await AuditRowsAsync(person)).Select(row => new Audit(row.ClientId, row.Result, row.Reason)).ToList();

    private sealed record Audit(string? ClientId, string Result, string? Reason);

    /// <summary>One host for the class: the test client is the first product's, <c>e2e-second-app</c> the second's.</summary>
    public sealed class Fixture : IAsyncLifetime
    {
        /// <summary>Gets the shared host.</summary>
        public OidcHost Server { get; } = new(new Dictionary<string, string?>
        {
            ["Identity:Server:Issuer"] = "https://localhost/",
            ["Identity:AccountDeletion:Audience"] = Audience,
            [$"Identity:AccountDeletion:Clients:{FirstDeleter}:ProductClientIds:0"] = OidcHost.ClientId,
            [$"Identity:AccountDeletion:Clients:{SecondDeleter}:ProductClientIds:0"] = SecondApp,
            ["Identity:Server:Resources:0:Name"] = "account-deletion",
            ["Identity:Server:Resources:0:Uri"] = Audience,
            ["Identity:Server:Resources:0:Scopes:0"] = DeleteScope,
            ["Identity:Server:Clients:1:ClientId"] = FirstDeleter,
            ["Identity:Server:Clients:1:ClientSecret"] = Secret,
            ["Identity:Server:Clients:1:Grants:0"] = "client_credentials",
            ["Identity:Server:Clients:1:Resources:0"] = "account-deletion",
            ["Identity:Server:Clients:1:Scopes:0"] = DeleteScope,
            ["Identity:Server:Clients:2:ClientId"] = SecondDeleter,
            ["Identity:Server:Clients:2:ClientSecret"] = Secret,
            ["Identity:Server:Clients:2:Grants:0"] = "client_credentials",
            ["Identity:Server:Clients:2:Resources:0"] = "account-deletion",
            ["Identity:Server:Clients:2:Scopes:0"] = DeleteScope,
            ["Identity:Server:Clients:3:ClientId"] = Stranger,
            ["Identity:Server:Clients:3:ClientSecret"] = Secret,
            ["Identity:Server:Clients:3:Grants:0"] = "client_credentials",
            ["Identity:Server:Clients:3:Resources:0"] = "account-deletion",
            ["Identity:Server:Clients:3:Scopes:0"] = DeleteScope,
            ["Identity:Server:Clients:4:ClientId"] = SecondApp,
            ["Identity:Server:Clients:4:ClientSecret"] = Secret,
            ["Identity:Server:Clients:4:RedirectUris:0"] = "https://second.example.test/signin-oidc",
        });

        Task IAsyncLifetime.InitializeAsync()
        {
            // Building a client starts the host: migrations, the key check and the client seeding.
            using var client = Server.Host.CreateClient();
            return Task.CompletedTask;
        }

        async Task IAsyncLifetime.DisposeAsync() => await Server.DisposeAsync();
    }
}
