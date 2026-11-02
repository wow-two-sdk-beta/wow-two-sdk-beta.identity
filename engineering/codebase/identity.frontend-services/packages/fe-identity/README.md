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
| `./kit` | `createAccountClient` (with `getSignInOptions`, `startInviteSignUp`, `getInviteSignUpStatus`), `readIdentityCode`, `readTwoFactorChallenge`, `passwordRule`, `codeRule`, `inspectPicture`; `PasswordSignIn`, `PhoneCodeSignIn`, `SignUpForm`, `InviteSignUpForm`, `ForgotPasswordForm`, `ResetPasswordForm`, `ChangePasswordForm` |

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
