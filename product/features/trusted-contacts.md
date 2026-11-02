# Trusted contacts

*Last updated: 2026-11-03*

> Another person of the instance receives a person's sign-in codes, for when the person has no phone of their own yet or
> it is out of reach. Asks and rules: the developer, 2026-11-02 and 2026-11-03; first requirements from `ocharo-platform`,
> 2026-11-03. Build: v0.5 iteration 10 (slice 1).

## Case

- An Ocharo owner invites mft as `owner` and names themselves her trusted contact, "so that we don't have to bother her".
- She signs up with a code sent to the owner's Telegram and has no phone of her own on the account.
- The owner signs in to her account with their own number; mft adds her own phone later.

## Model

- A trusted contact is another person of the instance whose confirmed, bot-linked phone receives codes for an account.
- It grants full sign-in to the account, the same power as the account's own phone; every place that adds one says so.
- The contact's number is the trusted contact number: the same number they sign in with, in a second role.
- `identity_trusted_contacts`: user, contact, created by, created at; one row per pair; at most `MaxPerPerson` per user.
- A person qualifies as a contact only while their own phone is confirmed and linked on the current bot.
- Slice 1 refuses a trusted contact on an account that holds an instance role.

## Invitation

- The console's invitation form has an optional "Trusted contact" picker: "Me", or another member with a confirmed phone.
- `POST /api/organizations/{id}/invitations` takes `trustedContactUserId`; the inviter's choice needs no other consent.
- The invitee gives no phone number for this; an invitation may still bind one, as today.

## Sign-up

- The invitation page asks for the name and picture, then how to verify the sign-up.
- Own Telegram: today's bot link and shared contact; the account gets that number confirmed.
- Trusted contact: a code goes to the contact's Telegram and is entered on the page.
- The code finishes the sign-up in one transaction: person created with no phone, invitation redeemed, contact recorded.
- The new account then keeps signing in through that contact until it adds its own phone.

## Sign-in

- By the account's own number: the code goes to its own Telegram by default.
  - The page offers the account's contacts as masked labels; picking one sends the code there instead.
- By a trusted contact number: the code goes to the contact's own Telegram.
  - After the code, the page lists the accounts to sign in as: the contact's own and each account trusting them.
  - A contact trusted by nobody signs straight in, as today.
- The bot's text to a contact names the account and the app, and says to share the code only when signing in for them.
- The session records the contact it went through (`trusted_contact_user_id`); `amr` stays `otp`.
- The sign-in log records method `trusted_contact_code` and the contact.

## Notices and one-click revoke

- A notice goes out only when an account has its own phone and a sign-in goes through one of its contacts.
- It reaches the account's own chat: who received the code, when, which app.
- Its "Revoke trust" button, pressable only by the account's own Telegram user, removes that contact.
- The bot answers the press in place: the notice reads "Trust revoked" and loses its button.

## Own phone and revoking

- An account with no phone adds one from account security through a bot `add-phone` link, from any session.
- Changing an existing own phone needs a session signed in with that own phone.
- Revoking a trusted contact needs a session signed in with the own phone, or the notice's button.
- So an account with no own phone can't revoke its contacts.
- Revoking a contact ends every session started through it.
- A contact can remove themselves, unless they are the last contact of an account with no own phone.
- Managers set a contact only on an invitation, never on an existing member's account: that would be a takeover path.
- The console's members view shows each member's contacts and whether their phone is confirmed.

## Bot message cleanup

- Telegram lets a bot delete its own messages only within 48 hours; editing has no limit, and `sendMessage` has no TTL.
- Codes and notices are recorded in `identity_telegram_messages`, buttons in `identity_telegram_callbacks` (`009`).
- A code message is deleted once its code expires.
- A new notice for an account deletes that account's older notices in the chat and spends their buttons at once.
- A notice is deleted after `Identity:Telegram:NoticeLifetime` (default 1 day, below 48 hours).
- A message past 48 hours, after a missed sweep, is edited to a one-line stub with no buttons instead.

## API (slice 1)

Fixed for the backend and the hosted pages alike; a change goes through this file first. Account routes sit under
`/api/account`, answers in the `ApiResponse` envelope as today.

| Route | Change |
|---|---|
| `GET /api/organizations/{id}/members` | each member gains `isPhoneConfirmed` and `trustedContacts: [{ userId, displayName }]` |
| `POST /api/organizations/{id}/invitations` | body gains `trustedContactUserId?` |
| `GET invitations/{token}` | preview gains `trustedContactHint?`, the contact's masked name |
| `POST invitations/{token}/sign-up` | form gains `verification`: `telegram` (default) or `trusted-contact`; with the latter the answer has `isCodeSentToTrustedContact: true` and no link |
| `POST invitations/sign-up/verify` | new: `{ code, isPersistent }` → `204` with the session cookie |
| `POST phone-sign-in/targets` | new: `{ phone }` → `{ trustedContacts: [{ id, label }] }`, masked labels and opaque ids; empty when none |
| `POST phone-sign-in/send` | body gains `targetId?`, one of those ids; without it, today's behaviour |
| `POST phone-sign-in` | when the number's person is a trusted contact, `200` with `accountChoice: { token, accounts: [{ userId, displayName, pictureUrl?, isSelf }] }` and no session |
| `POST phone-sign-in/choose` | new: `{ token, userId, isPersistent }` → `204` with the session cookie, or the token answer for bearer clients |
| `GET manage/trusted-contacts` | new: `{ canRevoke, items: [{ userId, displayName, phoneHint, isDeliverable, createdAt }] }` |
| `DELETE manage/trusted-contacts/{userId}` | new: `204`; `403 main_phone_required` outside an own-phone session |
| `GET manage/trusted-by` | new: `[{ userId, displayName, createdAt, canLeave }]` |
| `DELETE manage/trusted-by/{userId}` | new: `204`; `409 last_trusted_contact` for the last contact of an account with no own phone |
| `GET manage/phone` | new: `{ phoneHint?, isConfirmed, canChange }` |
| `POST manage/phone/telegram-link` | new: `{ telegramLinkUrl }` for the `add-phone` start link; `403 main_phone_required` when changing outside an own-phone session |
| `GET manage/sessions` | each session gains `trustedContactDisplayName?` |
| `GET sign-in-options` | gains `isTrustedContactSignInEnabled` |

## Settings

- `Identity:TrustedContacts:IsEnabled` (default off) and `MaxPerPerson` (default 3).
- `Identity:Telegram:NoticeLifetime` (default `1.00:00:00`, below 48 hours).
- Bot, hosted page and console texts come in `en`, `ru` and `uz`.

## Later

- A new device or a Telegram change waiting for a contact's confirmation (developer, 2026-11-03; meaning open).
