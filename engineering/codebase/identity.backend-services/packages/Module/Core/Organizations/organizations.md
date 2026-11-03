# Identity.Core.Organizations

Organizations as an identity slice: one person, many organizations, one role per membership. Stored in
`identity_organizations` and `identity_memberships`; principals carry each membership's role and permissions.

## Quick start

```csharp
identity.AddOrganizations(o =>
{
    o.Roles["editor"] = ["can-edit"];                     // the owner role ("owner") always exists
    o.Roles["viewer"] = [];
    o.Roles["owner"] = ["can-edit", "can-approve"];       // optional: permissions for the owner too
});

var ocharo = new IdentityOrganization { Name = "Ocharo", Slug = "ocharo" };
await organizations.CreateAsync(ocharo, founder);          // the creator becomes owner
await organizations.AddMemberAsync(ocharo, member, "editor");
await organizations.ChangeRoleAsync(ocharo, member, "viewer");

principal.GetOrganizationRole(ocharo.Id);                  // "viewer"
principal.HasOrganizationPermission(ocharo.Id, "can-edit");
```

```jsonc
"Identity": { "Organizations": { "Roles": { "editor": [ "desk:orders:edit" ], "viewer": [ "desk:orders:read" ] } } }
```

## Notes

- Role names compare case-insensitively and are stored as configured; an unconfigured role fails `UnknownOrganizationRole`.
- An organization may have no owner: removing or demoting the last one succeeds, and an admin may promote one later.
- Joining (a member added, an invitation redeemed), changing a role, removing a member and deleting the organization
  sign each member concerned out everywhere (`RoleChangeSignOut`, `../Roles/roles.md` § Role changes): every session
  ends as `roles_changed`, and they sign in again carrying the new `org_role` and `org_permission`. Re-roling to the same
  role changes nothing; creating an organization leaves its creator signed in, the new membership showing from their
  next sign-in.
- Claims are `org_role` = `{organizationId}:{role}` and `org_permission` = `{organizationId}:{permission}`; the
  types follow `IdentityClaimOptions` and the principal extensions take the type when a host changes it.
- Handles are unique, 1–64 lowercase letters, digits and single hyphens; names are 1–120 characters.
- Personal-data export adds an `organizations` section; account deletion removes the user's memberships, and an
  organization whose only owner is deleted keeps its other members without an owner.
- Existing databases need a migration for `identity_organizations` and `identity_memberships`.
- Organizations are made in the service's console by the instance admin (`admin create`, `../Admin/admin.md`), who
  becomes their owner; members join by invitation link (`../Invitations/invitations.md`) and are managed through
  `MapOrganizationEndpoints`.
- The roles are a fixed catalog (`OrganizationRoleConstants`): `owner`, `admin`, `editor`, `viewer`, `billing`;
  `Identity:Organizations:Roles` only adds permissions to them, and a role outside the catalog refuses the boot.
- `owner` and `admin` manage members and invitations alike: either invites with any catalog role, promotes to or
  demotes from `owner`, removes anyone and resolves anyone's Telegram change; `editor`, `viewer` and `billing` manage
  nothing. No rule stops a manager changing their own role or leaving.

## Management API

`app.MapGroup("/api/organizations").MapOrganizationEndpoints()` — for the organization's owners and admins, signed in:

| Route | Does |
|---|---|
| `GET {organizationId}/members` | the members: `userId`, `displayName`, `userName`, `email`, `phoneNumber`, `role`, `joinedAt`, `hasPendingTelegramChange`, `isPhoneConfirmed`, `trustedContacts: [{ userId, displayName }]` |
| `PUT {organizationId}/members/{userId}` | `{ role }`: another role, `owner` included; `400` `UnknownOrganizationRole` |
| `DELETE {organizationId}/members/{userId}` | removes the member, the last owner included; `404` `UserNotMember` |
| `POST {organizationId}/invitations` | `{ role, phoneNumber?, email?, trustedContactUserId? }`: a new link, its `token` and `link` shown this once; the contact is the inviter or another member with a confirmed, bot-linked phone |
| `GET {organizationId}/invitations` | the invitations, newest first, with `status`; never a token |
| `DELETE {organizationId}/invitations/{invitationId}` | withdraws a pending one; `404` once used or withdrawn |
| `POST {organizationId}/members/{userId}/telegram-change/approve` | moves the member's phone link to the Telegram account that asked; `404` `TelegramChangeNotFound` when none waits |
| `POST {organizationId}/members/{userId}/telegram-change/deny` | drops the request, the link stays; `404` `TelegramChangeNotFound` when none waits |

- A person outside the organization gets `404` `OrganizationNotFound`, a member who is neither owner nor admin `403`
  `OrganizationManagerRequired`.
- A role change or removal signs the member out everywhere, their own included; failures carry `identityCode`.
- The Telegram change routes map only with the bot registered (`AddTelegramLinks`, part of `AddPhoneSignIn`). A change
  waits when another Telegram account shares a member's confirmed, already linked number, as a recycled number's new
  holder would (`../../Telegram/telegram.md`); `hasPendingTelegramChange` flags it for a day. Approving rotates nothing
  else; an owner or admin resolves anyone's, an owner's included.
- Managers set a trusted contact only on an invitation, never on an existing member's account
  (`../TrustedContacts/trusted-contacts.md`).
