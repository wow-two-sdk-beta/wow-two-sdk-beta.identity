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
- A role change signs the person out everywhere: adding or removing a role runs `RoleChangeSignOut`, which rotates the
  stamp, drops this host's cached stamp and revokes every sign-in session as `roles_changed`, so they sign in again
  carrying the new roles. A call that changes nothing (a role already held) ends nothing.
- Deleting a role removes its claims and memberships in the same save; the schema carries no foreign keys.
- Authorization stays with ASP.NET policies over the role claims.
- Instance roles: `InstanceRoleConstants` (`admin`, `support`, `auditor`) is the fixed catalog over the instance itself,
  held beside a person's organization role. `InstanceRoleService.GrantAsync` seeds a role's row on first use and is
  idempotent; `GetAsync` lists what a person holds, `GetHoldersAsync` who holds a role. Organization roles live in
  memberships, never in `identity_roles`.
- Instance roles open the service's console (policy `identity-console`; creating an organization there takes `admin`,
  policy `identity-console-admin`) and never enter an app's tokens: the OIDC server writes token claims from scratch and
  gives role claims no destination.
- The first `admin` comes from the host CLI's `admin create` (`Core/Admin`), once per instance; the grant signs that
  person out like any other role change.

## Role changes

`RoleChangeSignOut.ApplyAsync` is the one rule for a person whose roles changed, called by `UserRoleService` (add,
remove, so every instance role grant) and `OrganizationService` (member added, so every invitation redeemed and the
invitation sign-up; role changed; member removed; organization deleted). Creating an organization leaves its creator
signed in.

| What | Ends |
|---|---|
| Identity cookie | at its next request: its session row is revoked |
| An app's OIDC grant | its next refresh answers `invalid_grant`, and userinfo refuses its access token; an access token an API checks offline lives out its lifetime (10 min by default) |
| A BFF session | at its next refresh, which the refused grant ends |

- A registration without `AddSessions` only rotates the stamp: cookies end by the stamp check, refreshes by the OIDC
  token endpoint's stamp check.
- A sign-in after the change takes the new stamp and stays signed in: the invitation sign-up's poll, an invited phone
  sign-in, a person signing in again.
