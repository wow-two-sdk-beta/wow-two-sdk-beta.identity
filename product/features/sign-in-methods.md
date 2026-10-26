# Sign-in methods

*Last updated: 2026-10-26*

> Every way a person proves who they are, and which phase ships it.

| Method | Phase | Notes |
|---|---|---|
| phone + code via own Telegram bot | P1 | free; the member opens the bot once and shares their contact |
| Log in with Telegram (OIDC) | P1 | one tap; the `phone` scope returns a Telegram-verified number; same bot |
| phone + code via Telegram Gateway | P2 | no bot link; $0.01 per delivered code, paid in TON; Telegram words the text |
| phone + code via SMS or WhatsApp | P2 | adapters exist (Eskiz for Uzbekistan, Twilio, Vonage) |
| email + password | P2 | hosted pages over the existing SDK flow |
| email magic link, email code | P2 | exists in the SDK; needs email delivery (D13) |
| passkeys | P2 | passwordless default for owners and admins; operators get them in P1 |
| social: 17 registrations + generic OIDC | P2 | one client per provider per instance, linked to one account |
| enterprise: Microsoft Entra ID, Google Workspace, SAML 2.0 | P3 | per-organization connections |
| regional: Yandex ID, OneID (Uzbekistan e-gov) | P3 | OneID needs an integration agreement |
| device flow, client credentials, personal access tokens | P2 | CLIs, TVs, service-to-service |

## Code channels

- every code channel is configuration per instance and per client: own bot, Telegram Gateway, SMS or email, over
  the SDK's keyed OTP delivery handlers (D4, decided 2026-10-25).
- every channel but Gateway words its message through `IOtpMessageFormatter`: per-culture ICU templates, built-in
  `uz`, `ru`, `en`, overridable per instance; Gateway takes only the code.

## Open

- Q15 — SMS in Uzbekistan: Eskiz primary, Twilio and Vonage as fallbacks; Eskiz needs an Uzbek legal entity.
- Q16 — hosted social buttons: Google, Apple, Telegram, Microsoft, Yandex; drop Spotify, Reddit and X; VK moves to
  VK ID.
- Q17 — ForeverPin: a redirect with a Google hint, or one-tap kept by exchanging the Google ID token.
- Q23 — SAML, SCIM, device flow and WhatsApp may move to P3 or drop.
