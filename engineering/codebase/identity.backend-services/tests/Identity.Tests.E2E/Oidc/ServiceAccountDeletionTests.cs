using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;

namespace Identity.Tests.E2E.Oidc;

/// <summary>Exercises the deletion route through the real signer, service credentials, and stored sessions.</summary>
public sealed class ServiceAccountDeletionTests
{
    private const string Audience = "https://localhost/api/service/people";
    private const string DeleteScope = "accounts.delete";
    private const string ReadScope = "accounts.read";
    private const string Deleter = "e2e-account-deleter";
    private const string Other = "e2e-account-other-client";
    private const string Secret = "e2e-test-only-account-service-secret";
    private const string Password = "e2e-correct-horse-battery";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Delete_ShouldRequireTheDedicatedServiceAndScope_AndEndAllSessions(bool allowDelete)
    {
        await using var server = new OidcHost(new Dictionary<string, string?>
        {
            ["Identity:Server:Issuer"] = "https://localhost/",
            ["Identity:AccountDeletion:ClientId"] = Deleter,
            ["Identity:AccountDeletion:Audience"] = Audience,
            ["Identity:Server:Resources:0:Name"] = "account-deletion",
            ["Identity:Server:Resources:0:Uri"] = Audience,
            ["Identity:Server:Resources:0:Scopes:0"] = DeleteScope,
            ["Identity:Server:Resources:0:Scopes:1"] = ReadScope,
            ["Identity:Server:Clients:1:ClientId"] = Deleter,
            ["Identity:Server:Clients:1:ClientSecret"] = Secret,
            ["Identity:Server:Clients:1:Grants:0"] = "client_credentials",
            ["Identity:Server:Clients:1:Resources:0"] = "account-deletion",
            ["Identity:Server:Clients:1:Scopes:0"] = allowDelete ? DeleteScope : ReadScope,
            ["Identity:Server:Clients:2:ClientId"] = Other,
            ["Identity:Server:Clients:2:ClientSecret"] = Secret,
            ["Identity:Server:Clients:2:Grants:0"] = "client_credentials",
            ["Identity:Server:Clients:2:Resources:0"] = "account-deletion",
            ["Identity:Server:Clients:2:Scopes:0"] = DeleteScope,
        });
        using var first = server.CreateClient();
        using var second = server.CreateClient();
        using var service = server.CreateClient();
        var email = $"delete-{Guid.NewGuid():N}@example.test";
        var person = await first.SignUpAndSignInAsync(email, Password);
        await second.SignInAsync(email, Password);
        var firstTokens = await first.SignInToAppAsync();
        var secondTokens = await second.SignInToAppAsync();
        var path = $"/api/service/people/{person:D}";

        (await DeleteAsync(first.Http, path, null)).Should().Be(HttpStatusCode.Unauthorized);
        (await DeleteAsync(service.Http, path, firstTokens.AccessToken)).Should().Be(HttpStatusCode.Unauthorized);
        var differentClient = await TokenAsync(service.Http, Other, DeleteScope);
        (await DeleteAsync(service.Http, path, differentClient)).Should().Be(HttpStatusCode.Forbidden);

        var authorized = await TokenAsync(service.Http, Deleter, allowDelete ? DeleteScope : ReadScope);
        if (!allowDelete)
        {
            (await DeleteAsync(service.Http, path, authorized)).Should().Be(HttpStatusCode.Forbidden);
            using var active = await first.RefreshAsync(firstTokens.RefreshToken);
            active.StatusCode.Should().Be(HttpStatusCode.OK);
            return;
        }

        (await DeleteAsync(service.Http, path, authorized)).Should().Be(HttpStatusCode.NoContent);
        (await DeleteAsync(service.Http, path, authorized)).Should().Be(HttpStatusCode.NoContent);

        using var firstRefresh = await first.RefreshAsync(firstTokens.RefreshToken);
        using var secondRefresh = await second.RefreshAsync(secondTokens.RefreshToken);
        firstRefresh.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        secondRefresh.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await firstRefresh.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Be("invalid_grant");
        (await secondRefresh.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Be("invalid_grant");
        using var newlyRegistered = server.CreateClient();
        (await newlyRegistered.SignUpAndSignInAsync(email, Password)).Should().NotBe(person);
    }

    private static async Task<string> TokenAsync(HttpClient http, string clientId, string scope)
    {
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = Secret,
            ["resource"] = Audience,
            ["scope"] = scope,
        });
        using var response = await http.PostAsync("/connect/token", body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString()!;
    }

    private static async Task<HttpStatusCode> DeleteAsync(HttpClient http, string path, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, path);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request);
        return response.StatusCode;
    }
}
