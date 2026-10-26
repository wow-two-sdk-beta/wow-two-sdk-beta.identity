# flows/

*Last updated: 2026-10-26*

> Identity's user flows: how a person signs in and out, and how an admin manages members. Technical flows (forwards,
> form switches, logout delivery): `engineering/architecture/flows/flows.md`.

## Sign-in

### First sign-in through the bot (P1)

1. The admin has added the member by name, phone and role; no password exists.
2. The member opens an app and is sent to the instance's hosted sign-in page.
3. The member enters their phone; the page answers the same for known and unknown numbers.
4. The page shows "Open {brand} bot" with a `t.me/{bot}?start={one-time token}` link.
5. The bot asks the member to share their contact.
6. The bot checks the contact is the sender's own and matches the invited phone, then stores the link.
7. The bot sends a 6-digit code; the member enters it on the page.
8. The instance signs the member in and returns them to the app with an authorization code.

### Later sign-ins (P1)

- phone, then the code from the bot; or one tap on Log in with Telegram.
- after the first link, the account is found by the Telegram user id, never by the phone the token reports.

### Other methods (P2)

- phone + code over Telegram Gateway, SMS or email — the channel is the instance's and the client's configuration.
- email + password, email magic link, email code; passkeys; social providers linked to one account.
- a second factor or step-up prompt when the role or the action asks for one.

### Form 1 sign-in

- the product shows the sign-in screens itself, from `@wow-two-beta/fe-identity`, on its own origin.
- the module checks the credentials in process and issues the product's cookie; no redirect, no SSO.

---

## Sign-out

- the member signs out in one app → the app clears its cookie and follows the instance's end-session URL.
- the instance ends the session and tells every other app by back-channel logout; each app drops that session.
- "sign out everywhere" on the account page or in the console revokes every session and refresh token.

---

## Management

### Add a member (P1)

1. The admin enters name, phone or email, role and app access in the console.
2. The instance creates the person if new, records the invitation and returns a link.
3. The member's first sign-in accepts the invitation (the bot flow above).

### Change a member (P1)

- change role or app access → new claims at the member's next token refresh.
- suspend → sign-in to the organization's apps stops and their sessions end; reinstate restores access.
- unlink Telegram → the next sign-in needs a new bot link.

### Remove a member (P1)

1. The admin removes the member from the organization.
2. Every session and refresh token of that membership is revoked at once.
3. Apps sign the member out by back-channel logout and receive `MemberRemoved`.

### From a product (P1)

- a product's backend calls the Identity brokers — read members, invite, change role, revoke sessions — after it
  checks the acting user's permission; the actor travels with the call for the audit trail.
- a product's admin page imports the same `./admin` screens the console uses.
