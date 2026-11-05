# Identity.Core.TrustedContacts

Trusted contacts: another person of the instance whose confirmed, bot-linked phone receives an account's sign-in codes,
with the same power as the account's own phone. An invitation names one; the person signs up with a code sent to the
contact's Telegram and holds no phone; the account later signs in through the contact or adds its own phone through the
bot. Spec: `product/features/trusted-contacts.md` (slice 1).

## Quick start

```csharp
identity.AddPhoneSignIn().AddInvitations().AddInviteSignUp()
        .AddTrustedContacts();                         // TrustedContactService, OwnPhoneService, add-phone, the revoke button

var account = app.MapGroup("/api/account");
account.MapPhoneSignInEndpoints()                      // + phone-sign-in/targets · phone-sign-in/choose
       .MapInviteSignUpEndpoints()                     // + invitations/sign-up/verify
       .MapTrustedContactEndpoints();                  // manage/trusted-contacts · manage/trusted-by · manage/phone
```

```jsonc
"Identity": {
  "TrustedContacts": {
    "IsEnabled": true,                                 // default false; needs Identity:Telegram:IsEnabled
    "MaxPerPerson": 3,                                 // 1–10, default 3
    "IsNewDeviceConfirmationRequired": true            // default true; a new browser waits for a contact's press
  }
}
```

## Routes

| Route | Does |
|---|---|
| `POST /api/organizations/{id}/invitations` | `trustedContactUserId?`: the inviter or another member with a confirmed phone linked to the bot; `400` `InvalidTrustedContact` / `TrustedContactUnavailable`, `404` `TrustedContactsOff` |
| `GET /api/organizations/{id}/members` | each member gains `isPhoneConfirmed` and `trustedContacts: [{ userId, displayName }]` |
| `GET invitations/{token}` | `trustedContactHint?`: the contact's masked name (`S*** R***`), or masked phone |
| `POST invitations/{token}/sign-up` | form `verification`: `telegram` (default) or `trusted-contact`, which answers `{ isCodeSentToTrustedContact: true, expiresAt }` with no link; `400` `NoTrustedContact`, `409` `TrustedContactUnavailable` |
| `POST invitations/sign-up/verify` | `{ code, isPersistent }` with the sign-up cookie → `204` and the session cookie; `401` `InvalidPhoneCode` |
| `POST phone-sign-in/targets` | `{ phone }` → `{ trustedContacts: [{ id, label }] }`; empty when none |
| `POST phone-sign-in/send` | `targetId?` sends the code to that contact; `404` `TrustedContactNotFound`, `409` `TrustedContactUnavailable` |
| `POST phone-sign-in` | a contact's code signs in through them; a contact's own number answers `200` `{ accountChoice: { token, accounts } }` and no session |
| `POST phone-sign-in/choose` | `{ token, userId, isPersistent }` → `204` with the cookie, or the token answer; `401` `InvalidAccountChoice` |
| `POST phone-sign-in`, `phone-sign-in/choose` | a new device answers `200` `{ deviceConfirmation: { token, expiresAt, contacts } }` and no session; `409` `DeviceConfirmationUnavailable` when no contact's chat took the notice |
| `POST phone-sign-in/confirmation` | polled, not rate-limited: `{ token, isPersistent }` → `200` `{ status: "pending" }`, then signs in as `phone-sign-in`; `403` `DeviceConfirmationDenied`, `401` `DeviceConfirmationExpired` |
| `GET manage/trusted-contacts` | `{ canRevoke, items: [{ userId, displayName, phoneHint, isDeliverable, createdAt }] }` |
| `DELETE manage/trusted-contacts/{userId}` | `204`; `403` `MainPhoneRequired` outside an own-phone session |
| `GET manage/trusted-by` | `[{ userId, displayName, createdAt, canLeave }]` |
| `DELETE manage/trusted-by/{userId}` | `204`; `409` `LastTrustedContact` for the last contact of an account with no own phone |
| `GET manage/phone` | `{ phoneHint?, isConfirmed, canChange }` |
| `POST manage/phone/telegram-link` | `{ telegramLinkUrl }`, the bot's `add-phone` start link; `403` `MainPhoneRequired` when changing outside an own-phone session |
| `GET manage/sessions` | each session gains `trustedContactDisplayName?` |
| `GET sign-in-options` | gains `isTrustedContactSignInEnabled` |

Account routes sit under `/api/account`; answers use the `ApiResponse` envelope, failures carry `identityCode`.

## New devices

`DeviceConfirmationService` holds a sign-in by an account's own code on a browser it hasn't used until a contact presses Approve
([spec](../../../../../../../product/features/trusted-contacts.md#new-devices)).

- A browser is the `__Host-identity-device` cookie (`DeviceCookie`); only its SHA-256 is stored, per account.
- `SignInPersonAsync` of the phone routes runs the check after the code and before the sign-in, so a second factor still follows.
- A bot share joining a known person through an invitation signs nobody in on a new device; the person signs in by code.
- The notice names the app from settings and a fixed device label: `clientId` and the user agent are the signer's own words.
- Who is asked, what passes and the limits: the spec.

## Rules

- A person qualifies as a contact only while their phone is confirmed and linked to the current bot (`isDeliverable`).
- An invitation's contact is the inviter or another member with such a phone; the inviter's choice needs no other
  consent. A link naming a phone or an email still redeems through the contact: the code is the proof, and the new person
  holds no phone.
- Sign-up through the contact: the code goes to the contact's Telegram, worded with the name the person gave and the app.
  Typing it creates the person with no phone (user name `member-{guid}`), the sign-up's name and picture, redeems the
  invitation and records the contact (`created_by` = the inviter), in one transaction: a refused join undoes all of it,
  a wrong code stays counted.
- Sign-in by the account's own number: the code goes to its own chat by default; `targets` lists the account's deliverable
  contacts as masked labels with opaque ids (a keyed digest of account and contact), and `send` with one sends it there.
- Sign-in by a contact's own number: once their own code verifies, `phone-sign-in` answers the account choice: their own
  account first, then each account trusting them. A contact trusted by nobody signs straight in, as before. The token is a
  single-use 12-character code in the OTP store (scope `identity.trusted-contact.choice`, ten minutes); a pick the token
  does not offer leaves it unspent.
- A sign-in through a contact logs method `trusted_contact_code` with `trusted_contact_user_id`, starts a session naming
  the contact, and keeps `amr` `otp`.
- Notice: only when the account has its own phone linked to the bot, a sign-in through one of its contacts sends that
  chat a `contact-sign-in` notice (who, when, which app: the `clientId`, else `Identity:Otp:Messages:AppName`) with a
  "Revoke trust" button only the account's own Telegram user may press; the press removes the contact, ends the sessions
  through them, and the notice reads "Trust revoked" with no button (`TrustedContactRevokeHandler`, action
  `trusted-contacts.revoke`). Cleanup and button handling: `../../Telegram/telegram.md`.
- Own phone: a confirmed number. An own-phone session went through no contact and was started by a `phone_code` or
  `phone_contact` sign-in, read from the sign-in log row tied to the session (`OwnPhoneService`), so the sign-in log
  slice is required and a session past `Identity:SignInLog:RetentionDays` loses the mark.
- Changing an own phone, and revoking a contact other than by the notice's button, need an own-phone session; so an
  account with no own phone cannot revoke its contacts. Adding a first phone works from any session.
- The `add-phone` link (bot purpose `add-phone`) carries the session that asked for it; its completion checks again: a
  number another account holds confirmed is refused (`add-phone-taken`), and an account that gained an own phone
  meanwhile changes only from an own-phone session (`add-phone-failed`). Setting the phone rotates no stamp.
- A contact leaves from any session, unless they are the last contact of an account with no own phone.
- Revoking a contact, or the contact leaving, ends every session started through them (`trusted_contact_revoked`).
- An account holding an instance role has no contact in slice 1: none is recorded for it, `targets` lists none, and it
  is never offered in an account choice. A role granted later leaves its rows unused.
- Limits: every code a contact receives is keyed by the contact (OTP scope `identity.trusted-contact`), so the contact's
  30 s window and failure budget count every account's codes; each code is bound to its target (`account:{id}` or
  `sign-up:{id}`, `IOtpService.CreateBoundAsync`), so it verifies for nothing else. A send from an account's own number
  also needs that number's own window and failure budget to allow it. A number whose own code ran out of tries tries no
  contact's code.
- Account deletion removes the person's contacts and their place as anyone's contact.
- Bot texts (`contact-code-sign-in`, `contact-code-sign-up`, `contact-sign-in-notice`, `revoke-trust-button`,
  `trust-revoked`, `add-phone-*`) come in `en`, `ru` and `uz`; `Identity:Telegram:Messages:{culture}:{key}` overrides them.
  The contact's culture is the bot's `DefaultCulture`, since their Telegram language is unknown when the code goes out.
- Schema: `010-trusted-contacts` (`identity_trusted_contacts`, and `trusted_contact_user_id` on invitations, sessions and
  the sign-in log).

## Open

- A bearer sign-in through a contact has no session row, so revoking the contact ends its refresh family only at the next
  stamp rotation.
- A sign-in through a contact that asks for a second factor finishes in `sign-in/two-factor`, whose session does not name
  the contact.
