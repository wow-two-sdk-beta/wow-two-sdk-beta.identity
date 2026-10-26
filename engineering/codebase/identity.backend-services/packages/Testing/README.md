# WoW2.Sdk.Beta.Be.Identity.Testing

The Identity test kit:

- `FakeGreetingsBroker` — an in-memory `IGreetingsBroker` for a consumer's unit tests; it passes the conformance suite.
- `GreetingsBrokerConformance` — the contract's suite: derive a test class, supply the broker and its clock, and every
  rule of the contract runs against that implementation.

```bash
dotnet add package WoW2.Sdk.Beta.Be.Identity.Testing --prerelease
```
