# WoW2.Sdk.Beta.Be.Identity.Abstractions

The Identity contract: `IGreetingsBroker`, `GreetingModel`, `GreetingConstants`, `GreetingRouteConstants` and the
`GreetingErrors` catalogue. A product's Domain and Application projects reference this package only; its composition
root picks an implementation:

- `WoW2.Sdk.Beta.Be.Identity.Module` — Form 1, in process, over the product's own database.
- `WoW2.Sdk.Beta.Be.Identity.Client` — Forms 2 and 3, over the running Identity service.

```bash
dotnet add package WoW2.Sdk.Beta.Be.Identity.Abstractions --prerelease
```
