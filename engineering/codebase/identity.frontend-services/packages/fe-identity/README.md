# @wow-two-beta/fe-identity

The Identity domain's UI: one component per sign-in method and password step, the same ones the service's hosted
pages render. Each calls the consumer's own backend (Form 1, the default same-origin client) or the running Identity
service (Forms 2 and 3, `baseUrl`), and signs in by cookie session.

```bash
pnpm add @wow-two-beta/fe-identity @wow-two-beta/fe vue
```

```vue
<script setup lang="ts">
import { PasswordSignIn, PhoneCodeSignIn } from '@wow-two-beta/fe-identity/kit';
</script>

<template>
  <PasswordSignIn base-url="https://id.example.com" @signed-in="onSignedIn" />
  <PhoneCodeSignIn base-url="https://id.example.com" :countries="['UZ']" @signed-in="onSignedIn" />
</template>
```

The components are styled with Tailwind utilities on the fe sdk theme; add the package to the consumer's Tailwind
sources. Messages resolve through the fe sdk `LocaleProvider` under `IdentityKit.*`, with English fallbacks.

```css
@source '../../node_modules/@wow-two-beta/fe-identity/dist';
```

| Export | Holds |
|---|---|
| `./contracts` | the account API's request and answer shapes, the sign-in declaration (`SignInOptionsDto`, `SignInMethodIdConstants`, `SignUpProfileDto`, `ProfileFieldMode`), `AccountRouteConstants`, `HostedPageRouteConstants`, `IdentityErrorCodeConstants`, `InviteSignUpStatus`, `PasswordChangeConfirmationType` |
| `./kit` | `createAccountClient` (with `getSignInOptions`, `startInviteSignUp`, `getInviteSignUpStatus`, `verifyInviteSignUp`, `getPhoneSignInTargets`, `choosePhoneSignInAccount`, `confirmPhoneSignIn`, `getPhone`, `createPhoneTelegramLink`, `getTrustedContacts`, `revokeTrustedContact`, `getTrustedBy`, `leaveTrustedContact`, `getSessions`), `readIdentityCode`, `isIdentityCode`, `readTwoFactorChallenge`, `passwordRule`, `codeRule`, `inspectPicture`; `PasswordSignIn`, `PhoneCodeSignIn`, `SignUpForm`, `InviteSignUpForm`, `ForgotPasswordForm`, `ResetPasswordForm`, `ChangePasswordForm`, `PhoneSettings`, `TrustedContactsList`, `TrustedByList`, `SessionList` |
| `./admin` | `createOrganizationClient`; `MembersPanel` and `InvitationsPanel` |

- `getSignInOptions({ clientId, locale })` reads `GET /api/account/sign-in-options`: the hosted pages' look, whether
  sign-up is open, and each enabled method in layout order with its fields and fe sdk rules, the messages in the
  request's locale. The look is the instance's, one for every client; `clientId` goes to the sign-in log.
- a cross-origin `baseUrl` sends the session cookie (`credentials: 'include'`); the service's CORS policy must allow
  the consumer's origin.
- `PhoneCodeSignIn` shows an "Open Telegram" step when the phone has no linked chat; the bot sends the code once the
  person shares the contact. Its `codeRules`, the declared `code` field's rules, block a code of the wrong length
  before it spends an attempt.
- `InviteSignUpForm` signs a person up through an invitation link with no code typed: the name and picture the
  declaration's `signUpProfile` asks for, then "Verify phone in Telegram". Sharing the contact with the bot proves the
  phone; the form polls every 2 s and emits `signedUp` once the session is set, or `signInRequired` when the person must
  sign in, such as with a second factor. A picture is checked before it is sent: type, size and, where the browser
  decodes it, its sides.
- `PasswordSignIn` and `PhoneCodeSignIn` continue to a second-factor step, a code or a recovery code, when the account
  has one (`TwoFactorRequired`), and emit `signedIn` after it.
- `SignUpForm`, `ForgotPasswordForm` and `PasswordSignIn` (its confirmation resend) take a `returnUrl`, a local path the
  emailed link carries back to the pages.
- `ChangePasswordForm` follows the instance's confirmation: the form at once (`None`), the form with a sent code
  (`Code`), or a sent link (`Link`) whose page passes the link's `token`. A sign-in whose password breaks the current
  rules emits `passwordChangeRequired` with a `ticket` and sets no session: pass `ticket` and `confirmation` to the
  form, then sign in again with the new password.

Trusted contacts (another person of the instance who receives a person's sign-in codes; `Identity:TrustedContacts`):

- `PhoneCodeSignIn` with `isTrustedContactSignInEnabled` (the declaration's flag) checks the phone's contacts after the
  number: a phone that has some offers "My Telegram", the default, or each contact's masked label as the place the code
  goes (`targetId` of the send). A phone whose person is another's trusted contact continues, after the code, to the
  accounts it can sign in as (`accountChoice`), and `signedIn` fires once one is picked.
- `InviteSignUpForm` with the preview's `trustedContactHint` offers to verify the sign-up by the person's own Telegram,
  the default, or by that contact: the form sends `verification=trusted-contact`, the contact receives a code in
  Telegram, and the code typed on the form (`verifyInviteSignUp`) finishes the sign-up with no phone on the new account.
  It takes `codeRules` and `isPersistenceOffered` for that code step.
- `PhoneSettings` adds or changes the signed-in account's own phone through the bot's `add-phone` link and polls until
  the number shows, or fires `signedOut` once a changed number ended the session; `TrustedContactsList` lists the account's contacts and revokes one (`canRevoke`, from a session
  signed in with the own phone); `TrustedByList` lists the accounts that trust the person and leaves one (`canLeave`);
  `SessionList` lists the live sessions and the contact one went through. Each renders nothing while its route answers
  `404`.
- `InvitationsPanel` takes `currentUserId` and offers an optional trusted contact on the invitation form: "Me", or a
  member whose `isPhoneConfirmed` is true, with a full-access warning. `MembersPanel` shows each member's phone state and
  contacts.
- `isIdentityCode(failure, IdentityErrorCodeConstants.mainPhoneRequired)` reads the `main_phone_required` and
  `last_trusted_contact` refusals in either `snake_case` or `PascalCase`.
