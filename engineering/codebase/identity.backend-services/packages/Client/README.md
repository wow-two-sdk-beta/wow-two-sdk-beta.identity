# WoW2.Sdk.Beta.Be.Identity.Client

Forms 2 and 3 of the Identity capability: `IGreetingsBroker` over the running Identity service's HTTP API. The product
changes form by configuration: swap `AddIdentity()` for `AddIdentityClient(…)` in its composition root; no other code
changes.

```bash
dotnet add package WoW2.Sdk.Beta.Be.Identity.Client --prerelease
```

```csharp
services.AddIdentityClient(options => options.BaseAddress = new Uri("https://identity.example.com/"));
```
