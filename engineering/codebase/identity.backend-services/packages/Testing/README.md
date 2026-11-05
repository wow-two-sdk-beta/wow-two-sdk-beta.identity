# WoW2.Sdk.Beta.Be.Identity.Testing

The Identity test kit. Today it holds the fake token issuer; fakes over the contract, personas, the OTP code sink and the
broker conformance bases land once the contract's brokers exist. It references the contract, `WoW2.Sdk.Beta.Be.Identity.Auth`
and `WoW2.Sdk.Beta.Be.Testing`.

```bash
dotnet add package WoW2.Sdk.Beta.Be.Identity.Testing --prerelease
```

## Fake token issuer

`FakeTokenIssuer` signs person, service and exchanged tokens in the exact shape Identity issues, with an in-memory RSA key,
so a consumer tests with no Identity running.

```csharp
using var issuer = new FakeTokenIssuer();

await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
    host.ConfigureTestServices(services => services.AddFakeTokenIssuer(issuer)));
var client = factory.CreateClient();

// The audience is the host's real one: a wrong one still answers 401.
var worker = issuer.IssueServiceToken("ocharo-editorial", "https://files.example.com/", "files.read");
client.DefaultRequestHeaders.Authorization = new("Bearer", worker);
await client.GetAsync("/files");

var person = new FakePerson { Id = Guid.NewGuid(), OrganizationId = organizationId, Role = "owner", Permissions = ["files:share"], SessionId = Guid.NewGuid() };
var own = issuer.IssuePersonToken(person, "https://files.example.com/", scopes: "files.read");
var exchanged = issuer.IssueExchangedToken(person, "ocharo-editorial", "https://files.example.com/", "files.read");
```

- `AddFakeTokenIssuer` makes the default `JwtBearer` scheme trust only the fake. A host's metadata address, trusted issuers and resolver stop being read, so nothing reaches a network; its audiences stay.
- `issuer.AsTrustedIssuer(partition, audiences)` is the other way in: add it to `JwtOptions.TrustedIssuers` when a test needs the real trusted-issuer pipeline, a stamped `partition` included.
- `Lifetime` defaults to 5 minutes; `clock` dates the tokens, so a past clock makes an expired token.

### A caller under test

`CreateTokenEndpointHandler()` answers form posts as Identity's `/connect/token` does, for `client_credentials` and token exchange. Swap it in as the primary handler of the named `HttpClient` the be sdk's token handler posts through, or use a plain `HttpClient`:

```csharp
using var tokens = new HttpClient(issuer.CreateTokenEndpointHandler());
var response = await tokens.PostAsync("https://fake-issuer.test/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
{
    ["grant_type"] = "client_credentials",
    ["client_id"] = "ocharo-editorial",
    ["client_secret"] = "any",
    ["resource"] = "https://desk.example.com/",
    ["scope"] = "desk.media",
}));
```

The handler has no registry: any client may ask for any resource and scope, a client secret isn't checked, an omitted `scope` grants none, and an exchanged token keeps every permission. A failure answers `400` with `{ "error": … }`, under the same error codes as Identity.
