# Identity.Core.Roles

Roles slice: role administration (`RoleService`), membership by role name (`UserRoleService`) and role claims.
Principals built by `UserClaimsPrincipalFactory` carry role names and role claims once this slice is registered.

## Quick start

```csharp
identity.AddRoles();                                      // the role repositories over IdentityDbContext

await roles.CreateAsync(new IdentityRole { Name = "Admin" });
await roles.AddClaimAsync(admin, new Claim("permission", "users.write"));
await memberships.AddToRoleAsync(user, "admin");          // names are case-insensitive
```

## Notes

- `AddRoles()` is a builder method; roles live in `identity_roles`, ids are uuid v7 assigned by `RoleService.CreateAsync`.
- A role change signs nobody out (§ Role changes).
- Deleting a role removes its claims and memberships in the same save; the schema carries no foreign keys.
- Authorization stays with ASP.NET policies over the role claims.
- Instance roles: `InstanceRoleConstants` (`admin`, `support`, `auditor`) is the fixed catalog over the instance itself,
  held beside a person's organization role. `InstanceRoleService.GrantAsync` seeds a role's row on first use and is
  idempotent; `GetAsync` lists what a person holds, `GetHoldersAsync` who holds a role. Organization roles live in
  memberships, never in `identity_roles`.
- Instance roles open the service's console (policy `identity-console`; creating an organization there takes `admin`,
  policy `identity-console-admin`) and never enter an app's tokens: the OIDC server writes token claims from scratch and
  gives role claims no destination.
- The first `admin` comes from the host CLI's `admin create` (`Core/Admin`), once per instance.

## Role changes

A role change signs nobody out; the person keeps working and the next token or request carries the new answer (developer,
2026-11-05; it replaced signing out everywhere).

| What | Takes the change |
|---|---|
| Identity cookie | on its next request: `CurrentAccessClaimsTransformation` rebuilds its roles and permissions from the store |
| An app's OIDC tokens | at the next refresh, which writes fresh claims; a BFF hearing the signal refreshes at once |
| An access token an API checks offline | the signal puts it on the API's deny list; with no signal it lives out its lifetime (10 min by default) |

- `OrganizationService` tells every `IAccessChangeHandler` (`AccessChangedEvent`): `Changed` for a member added or re-roled,
  `Removed` for a member removed or their organization deleted. A call that changes nothing tells nobody.
- Instance roles never enter an app's token, so they raise no signal.
- `roles_changed` stays a reason on old session rows; nothing new writes it.
- Signals: [service tokens](../../../../../../../product/features/service-tokens.md).
