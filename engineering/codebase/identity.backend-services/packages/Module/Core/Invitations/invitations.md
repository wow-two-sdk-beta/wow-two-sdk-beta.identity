# Identity.Core.Invitations

Invitations into an organization: an owner or admin makes a single-use link carrying a role the person cannot change.
It signs a new person up, even while sign-up is by invitation only (`Identity:SignUp:Mode` `Invite`, the default), or
adds a signed-in person, with that role. One row per invitation in `identity_invitations` (`006-invitations`).

## Quick start

```csharp
identity.AddOrganizations().AddInvitations().AddAccountEndpoints();

app.MapGroup("/api/account").MapUserAccountEndpoints();           // invitations/{token}, invitations/{token}/accept
app.MapGroup("/api/organizations").MapOrganizationEndpoints();    // {organizationId}/invitations for owners and admins
```

```jsonc
"Identity": {
  "SignUp": { "Mode": "Invite" },              // Invite (default): only an invitation signs up; Open: anyone
  "Invitations": {
    "Lifetime": "7.00:00:00",                  // how long a link redeems
    "LinkTemplate": "https://id.example.com/sign-up?invitation={token}"  // the service sets it from Identity:Server:Issuer
  }
}
```

## Lifecycle

| Step | Route or call | Does |
|---|---|---|
| Make | `POST /api/organizations/{organizationId}/invitations` `{ role, phoneNumber?, email? }` | stores the token's SHA-256, answers the `token` and `link` once |
| Preview | `GET /api/account/invitations/{token}` | anonymous: `organizationName`, `role`, `status`, `phoneNumberHint`, `emailHint`, `expiresAt`; `404` for a link naming none |
| Sign up | `POST /api/account/sign-up` with `invitationToken` | creates the account and joins it with the role, in one transaction |
| Sign in by phone | `POST /api/account/phone-sign-in` with `invitationToken` | creates the person on a first sign-in, or takes the known one, and joins them |
| Accept | `POST /api/account/invitations/{token}/accept` | joins the signed-in person and reissues their cookie with the new role |
| Withdraw | `DELETE /api/organizations/{organizationId}/invitations/{invitationId}` | a pending one only |

## Rules

- Single use: redemption is a conditional update, so one invitation joins one person; a member already keeps their role
  and spends it.
- A link naming a phone number redeems only for that confirmed number; one naming an email only for that email, which
  the instance's sign-in rules may still ask the person to confirm. A link naming neither takes anyone holding it.
- A refused redemption undoes the sign-up's writes (`RedeemWithAsync`); a failed code attempt stays counted.
- Refusals: `404` `InvitationNotFound`; `409` `InvitationExpired`, `InvitationAccepted`, `InvitationRevoked`; `403`
  `InvitationIdentifierMismatch`; `400` `UnknownOrganizationRole`, `InvalidInvitationIdentifier`. Each carries
  `identityCode`.
- The hosted page `/sign-up?invitation={token}` previews the link and offers the instance's methods carrying the token,
  or a "Join" button to someone signed in.
