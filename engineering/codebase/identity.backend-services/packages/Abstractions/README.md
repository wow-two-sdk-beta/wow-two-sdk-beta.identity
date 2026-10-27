# WoW2.Sdk.Beta.Be.Identity.Abstractions

The Identity contract: `IdentityErrorCodeConstants` and their ru/uz texts (`IdentityErrorTextConstants`), the
organization claim types and readers (`OrganizationClaimTypeConstants`, `OrganizationClaimsPrincipalExtensions`), the
`amr` values a sign-in records (`AuthenticationMethodConstants`) and the session claim `sid` (`SessionClaimTypeConstants`). It references the kernel `WoW2.Sdk.Beta.Be.Abstractions`
only. A product's Domain and Application projects reference this package; its composition root picks an
implementation:

- `WoW2.Sdk.Beta.Be.Identity.Module` — Form 1, in process, over the product's own database.
- `WoW2.Sdk.Beta.Be.Identity.Client` — Forms 2 and 3, against a running Identity instance.

```bash
dotnet add package WoW2.Sdk.Beta.Be.Identity.Abstractions --prerelease
```

Namespaces are the ones these types had in the be sdk (`WoW.Two.Sdk.Beta.Be.Identity.Core`, `…Core.Organizations`).
