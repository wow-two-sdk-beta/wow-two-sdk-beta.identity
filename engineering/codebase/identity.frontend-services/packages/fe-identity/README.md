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
| `./contracts` | the account API's request and answer shapes, the sign-in declaration (`SignInOptionsDto`, `SignInMethodIdConstants`), `AccountRouteConstants`, `HostedPageRouteConstants`, `PasswordChangeConfirmationType` |
| `./kit` | `createAccountClient` (with `getSignInOptions`); `PasswordSignIn`, `PhoneCodeSignIn`, `SignUpForm`, `ForgotPasswordForm`, `ResetPasswordForm`, `ChangePasswordForm` |

- `getSignInOptions({ clientId, locale })` reads `GET /api/account/sign-in-options`: the hosted pages' look, whether
  sign-up is open, and each enabled method in layout order with its fields and fe sdk rules, the messages in the
  request's locale. The look is the instance's, one for every client; `clientId` goes to the sign-in log.
- a cross-origin `baseUrl` sends the session cookie (`credentials: 'include'`); the service's CORS policy must allow
  the consumer's origin.
- `PhoneCodeSignIn` shows an "Open Telegram" step when the phone has no linked chat; the bot sends the code once the
  person shares the contact.
- `ChangePasswordForm` follows the instance's confirmation: the form at once (`None`), the form with a sent code
  (`Code`), or a sent link (`Link`) whose page passes the link's `token`. A sign-in whose password breaks the current
  rules emits `passwordChangeRequired` with a `ticket` and sets no session: pass `ticket` and `confirmation` to the
  form, then sign in again with the new password.
