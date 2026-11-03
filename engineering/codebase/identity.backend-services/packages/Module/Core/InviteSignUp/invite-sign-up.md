# Identity.Core.InviteSignUp

Sign-up from an invitation with the phone the instance's bot verifies, no code typed.

## Flow

1. The invitation link opens the hosted sign-up page; it asks for the profile `Identity:SignUp:Profile` names.
2. `POST /api/account/invitations/{token}/sign-up` (`multipart/form-data`: `displayName`, `picture`) checks the
   invitation and the profile, processes the picture, stores the waiting sign-up and answers the bot's start link,
   setting the HttpOnly `__Host-identity-sign-up` cookie (`{id}.{secret}`; only the secret's SHA-256 is stored).
3. The person opens the link; the bot asks for their own contact (`invite-sign-up` purpose); the contact proves the
   number and links the chat; an invitation naming a phone accepts only that number.
4. `InviteSignUpService.CompleteAsync` finds or creates the person holding the number confirmed, gives a new person the
   name and picture (a known one keeps theirs), and redeems the invitation, in one transaction.
5. The starting browser polls `GET /api/account/invitations/sign-up/status`; the first `completed` answer signs it in
   with a cookie session (`amr` `otp`, sign-in log method `phone_contact`), unless a second factor is due.

## Through a trusted contact

With trusted contacts registered and the invitation naming a contact, the form's `verification` may be
`trusted-contact`: the start route sends a code to the contact's Telegram (worded with the name given and the app) and
answers `{ isCodeSentToTrustedContact: true, expiresAt }` with the same cookie and no link;
`POST invitations/sign-up/verify` `{ code, isPersistent }` then creates the person with no phone, the name and picture,
redeems the invitation and records the contact, in one transaction, and signs the browser in (method
`trusted_contact_code`, the session naming the contact). The invitation's own phone or email is not asked of the new
person. Rules: `../TrustedContacts/trusted-contacts.md`.

## Notes

- Register with `AddInviteSignUp()` beside `AddInvitations()`, `AddPhoneSignIn()` and `AddProfilePictures()`; map with
  `MapInviteSignUpEndpoints()` on the account API's group.
- A sign-up lasts the bot's `LinkLifetime`, never past its invitation; rows a day past expiry are pruned.
- A finished sign-up drops its picture bytes; the person's picture then lives in `IProfilePictureStore`.
