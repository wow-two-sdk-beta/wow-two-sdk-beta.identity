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
- Removing a membership rotates the security stamp so principals holding the role end; an added role appears at next sign-in.
- Deleting a role removes its claims and memberships in the same save; the schema carries no foreign keys.
- Authorization stays with ASP.NET policies over the role claims.
- Instance roles: `InstanceRoleConstants` (`admin`, `support`, `auditor`) is the fixed catalog over the instance itself,
  held beside a person's organization role. `InstanceRoleService.GrantAsync` seeds a role's row on first use and is
  idempotent; `GetAsync` lists what a person holds. Organization roles live in memberships, never in `identity_roles`.
- Instance roles open the service's console (policy `identity-console`) and never enter an app's tokens: the OIDC server
  writes token claims from scratch and gives role claims no destination.
