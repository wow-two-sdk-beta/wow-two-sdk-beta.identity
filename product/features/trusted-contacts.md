# Trusted contacts

*Last updated: 2026-11-03*

> Another person of the instance receives a person's sign-in codes, for when the person's own phone is out of reach.
> Asks: the developer, 2026-11-02 and 2026-11-03; requirements from `ocharo-platform`, 2026-11-03. Build: v0.5
> iteration 10 (slice 1).

## Case

- An Ocharo owner invites mft as `owner` and names themselves her trusted contact, "so that we don't have to bother her".
- Codes for her account go to the owner's Telegram; mft adds her own phone later.

## Model

- A trusted contact is another person of the instance whose confirmed, bot-linked phone receives codes for an account.
- It grants full sign-in to the account, the same power as the person's own phone; every place that adds one says so.
- `identity_trusted_contacts`: user, contact, created by, created at; one row per pair; at most `MaxPerPerson` per user.
- Delivery skips a contact whose own phone is unconfirmed or not linked on the current bot.
- Slice 1 refuses a trusted contact on an account that holds an instance role.

## Invitation with a trusted contact (slice 1: the inviter only)

- `POST /api/organizations/{id}/invitations` takes `trustedContactUserId`; slice 1 accepts only the caller's own id.
- The inviter qualifies only while their own phone is confirmed and linked on the current bot (developer, 2026-11-03).
- With a contact, `phoneNumber` is required and lands on the account pending (set, unconfirmed); nothing proves it yet. Open (2026-11-03): whether this number is how
  she signs in before her phone is confirmed.
- Consent: the inviter's choice applies with no step from the invitee; the person removes the contact later.
- Console form: an "I receive their codes" checkbox with the full-access warning.
- Sign-up page: after name and picture, "Send the code to {masked inviter}" replaces the bot link.
- Entering the code finishes the sign-up in one transaction: person created, invitation redeemed, contact recorded.

## Sign-in with the code sent elsewhere

- `phone-sign-in/send` takes `target`: `own` (default, today's flow) or `trusted-contact`.
- `trusted-contact` answers `isCodeSent: true` for every number, so it reveals nothing new about accounts.
- The code goes to each deliverable contact; slice 1 has one.
- A pending number signs in only through contacts; the bot's own start link never confirms a pending number.
- The bot's text to a contact names the account and the app, and says to share the code only when signing in for them.
- The session records the contact (`trusted_contact_user_id`); `amr` stays `otp`.
- The sign-in log records method `trusted_contact_code` and the contact.

## Notices and one-click revoke

- Every sign-in through a contact sends a notice to the person's own linked chat: who received the code, when, which app.
- The notice carries a "Revoke trust" button: one click removes that contact and ends every session started through it.
- The bot answers the click in place: the notice is edited to "Trust revoked" and loses its button.
- A click counts only from the Telegram user linked to the account (`callback_query.from.id`).
- Callback data is an opaque token of at most 64 bytes, hashed at rest, single use.
- The webhook and polling accept `callback_query` beside `message`.
- A person with no linked chat gets no notice; their account security page lists the contact sessions.

## Bot message cleanup

- Telegram lets a bot delete its own messages only within 48 hours; editing has no limit, and `sendMessage` has no TTL.
- Every code and notice the bot sends is recorded: `identity_telegram_messages` (bot, chat, message id, kind, user, sent
  at, delete at).
- A code message is deleted once its code expires (5 minutes).
- A new notice for an account deletes that account's older notices in the same chat.
- A notice is deleted after `NoticeLifetime` (default 24 hours, refused at 48 hours or more).
- A row past 48 hours, after a missed sweep, is edited to a one-line stub with no buttons instead.
- A background sweep runs each minute and deletes up to 100 messages per `deleteMessages` call.

## Add the real phone later

- Account security, signed in: "Add phone" makes a bot start link with purpose `add-phone`.
- The shared contact confirms that number on the signed-in account and links the chat, replacing a pending number.
- A number another person holds confirmed is refused; a pending number gives way when another person confirms it.
- Once confirmed, the own chat is the default target; contacts stay until removed.

## Management

- The person lists and removes their contacts on the account security page.
- A contact lists the accounts that trust them and removes themselves.
- Removing a contact ends every session started through it.
- An account with no confirmed phone keeps its last contact: removing it is refused, for the person and the contact alike
  (developer, 2026-11-03).
- Managers set a contact only on an invitation, never on an existing member's account: that would be a takeover path.
- The console's members view shows each member's contacts and whether their phone is confirmed.

## Settings

- `Identity:TrustedContacts:IsEnabled` (default off) and `MaxPerPerson` (default 3).
- `Identity:Telegram:NoticeLifetime` (default `1.00:00:00`, below 48 hours).
- Bot, hosted page and console texts come in `en`, `ru` and `uz`.

## Later

- Slice 2: any member of a shared organization as a contact, confirmed by the contact in the bot.
- A new device or a Telegram change waiting for a contact's confirmation (developer, 2026-11-03; meaning open).
