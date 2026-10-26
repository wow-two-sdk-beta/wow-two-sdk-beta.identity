# Developer surface

*Last updated: 2026-10-26*

> What a product's developer touches: packages, screens, hosted pages, APIs and test tools.

- the `Client` package: one registration, cookie session, claims, CSRF, back-channel logout (P1).
- `@wow-two-beta/fe-identity`: embeddable components and widgets — phone + code, Telegram link step, password with
  meter, provider buttons, passkey, MFA, account parts — or a forward of the flow to the hosted pages
  (P1 phone, code and link; the rest P2).
- the fe sdk's `accountMenu` reads the session and links to the hosted account page.
- hosted sign-in pages themed per client (`ocharo` for Ocharo) and localized in `uz`, `ru`, `en` (P1).
- test mode: fake channels, an in-memory code sink, persona sign-in and a test-host helper (P1, decided 2026-10-25).
- a claims-contributor hook: in-process in Form 1, a client callback in Forms 2 and 3 (P1, decided 2026-10-25).
- management API with scoped keys; webhooks such as `member.added` and `signed_in` (P2); SCIM (P3).
