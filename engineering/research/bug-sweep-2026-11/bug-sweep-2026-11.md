# Bug sweep — 2026-11

*Last updated: 2026-11-02*

> A read-only sweep of Identity before Ocharo signs in end to end: 19 findings, all fixed the same day in `a781e57`
> (backend) and `cb0b7c0` (frontend), shipped as NuGet `10.0.16-beta`, npm `fe-identity` `0.0.7` and image `v0.5.7`.
> The local test afterwards found a 20th (the role catalog), fixed in `2376611`. Track: v0.5 Iteration 8.

## Resolution

| # | Finding | Outcome |
|---|---|---|
| 1 | a password guess locks out a phone person, and the lockout ends their sessions | fixed differently: a lockout never ends sessions, refreshes or authorize, admin locks included (an admin ends sessions by rotating the stamp); an account without a password answers as an unknown one and counts nothing |
| 2 | phone codes guessable across resends | fixed: one code per number per 30 s, per address per 5 s, 10 wrong codes per hour per number, no code stored when delivery fails, `identity-credentials` rate limit (30 per minute per address) |
| 3 | Argon2 memory exhaustion | fixed: 4 hashes at once, 429 after 5 s queued; container memory 256 → 512 MB |
| 4 | BFF refresh on `RequestAborted` | fixed: refresh and save run on their own timeout |
| 5 | BFF in-memory sessions and keys | warned, not refused: a deployed boot logs a warning; products register a shared `IDistributedCache` and a persisted key ring (`packages/Client/README.md`) |
| 6 | concurrent wrong passwords lose lockout counts | fixed: an atomic increment |
| 7 | emailed links land on the console | fixed: `returnUrl` rides sign-up, resend and forgot-password into the emailed links and back |
| 8 | phone numbers enumerable | partly: the password timing leak is closed; `phone-sign-in/send` still tells a linked number apart, documented as accepted |
| 9 | `admin create --email` adopts an unconfirmed account | fixed |
| 10 | `prompt=login` and `max_age` not enforced | fixed: a sealed `fresh_sign_in` marker; `auth_time` from the session's start |
| 11 | logout CSRF | fixed: end-session revokes only with a valid `id_token_hint` or a same-origin POST |
| 12 | phone person refused under a confirmed-email rule | fixed differently: per account — no email plus a confirmed phone meets the rule |
| 13 | malformed sign-up email answers 500 after creating the account | fixed in Identity: 400 before any write; the be sdk's MailKit broker still builds the message outside its `try` (be sdk backlog) |
| 14 | BFF never refreshes signing keys | fixed |
| 15 | failed callback loops | fixed: a 10 s failure cookie and `signInError=callback_failed` |
| 16 | a superseded code works again | fixed differently: a new code moves older codes' expiry to now, so the failure budget still counts them |
| 17 | resend messages promise what was not sent | fixed: `EmailConfirmationRequired` and `PhoneConfirmationRequired` codes; the phone form returns to the Telegram step |
| 18 | code typos spend attempts | fixed: the kit checks the declared code rules first |
| 19 | the kit has no second-factor step | fixed: `SecondFactorForm` posts `sign-in/two-factor` |
| 20 | the role catalog lived only in settings: `editor`, `viewer`, `billing` refused unless configured | fixed: `OrganizationRoleConstants`; settings add permissions only, a role outside the catalog refuses the boot |

---

## Findings as reported
Read-only sweep, 2026-11-02, working tree at `f727598`. Nothing in the repo was edited.

### Overview

| Severity | Count | Findings |
|---|---|---|
| High | 2 | #1, #2 |
| Medium | 5 | #3 – #7 |
| Low | 12 | #8 – #19 |

- Paths are relative to `wow-two-sdk-beta.identity/engineering/`. `BE` = `codebase/identity.backend-services/packages/`, `FE` = `codebase/identity.frontend-services/`.
- Status: CONFIRMED = traced unambiguously or reproduced. PLAUSIBLE = the mechanism is shown, but the trigger was not reproduced in the target environment.
- The out-of-scope items you listed are not repeated here. Session-lifetime design is skipped; #10 is reported only because it is an outright bug.
- Sources: #1 – #3, #6, #8 – #10, #12, #13 and #16 come from my own pass over Module and the host. #4, #5, #11, #14 and #15 come from a Server/BFF sub-sweep. #7 and #17 – #19 come from an FE sub-sweep. I spot-checked their key lines.

---

### High

#### 1. Anyone can lock out any phone user, and the lockout kills their live sessions in every app — CONFIRMED

- **Where:**
  - `BE/Module/Core/SignIn/SignInService.cs:132-136` counts a failed password against an account that has no password.
  - `BE/Module/Core/PhoneSignIn/UserPhoneSignInService.cs:151` makes the phone number the user name.
  - `BE/Server/OidcServer/OidcServerEndpointRouteBuilderExtensions.cs:220-221` treats a locked-out user as gone, and `:104-107` answers `invalid_grant`.
  - `BE/Client/Bff/Sessions/BffSessionService.cs:205-206` ends the BFF session on that answer.
- **Defect:** a phone user's user name is their E.164 number (`UserName = phone`), and their lockout is on by default (`IsEnabledForNewUsers = true`). `POST /api/account/sign-in {identifier:"+99890…", password:"x"}` finds them by name. `CheckPasswordAsync` fails, because `PasswordHash` is null, and `RecordFailedAttemptAsync` counts it. Five calls lock the account for 5 minutes.
- **Scenario:**
  - An attacker who knows a customer's phone number sends 5 bad passwords every 5 minutes.
  - The customer's phone sign-in answers 429 `LockedOut`. The OTP is spent before the lockout check: `PhoneSignInEndpointRouteBuilderExtensions.cs:75` runs before `:89`.
  - Their open Ocharo tab's next refresh hits `/connect/token`, where `FindPersonAsync` returns null. The token endpoint answers `invalid_grant` 400, and the BFF classes it `Rejected` and deletes the session.
  - The lockout is temporary, but the sign-out is permanent: every app is signed out, and the customer cannot sign back in while the attack runs.
  - The same works against email/password users. Ending existing sessions is the amplifier.
- **Fix:**
  - Treat an account without a password hash like an unknown identifier: spend the decoy hash and do not count a lockout.
  - Scope lockout to new password attempts. Do not reject refresh or the authorize cookie for an automatic lockout; keep that only for an admin lock (a separate flag or `LockoutEnd = max`).
  - Check lockout before spending the OTP.

#### 2. Phone sign-in codes can be brute-forced at about one guess per second per number — CONFIRMED

- **Where:**
  - `BE/Module/Otp/OtpOptions.cs:20` (`RateLimitWindow` 5 s) and `:23` (`MaxAttempts` 5).
  - `BE/Module/Core/OneTimeCodes/EfOtpRepository.cs:26-29`: the rate limit only sees a recent *unconsumed* code from the last 5 s.
  - `BE/Module/Otp/OtpService.cs:105-119`: attempts are counted per code, not per number.
  - `BE/Module/Core/PhoneSignIn/UserPhoneSignInService.cs:65-70,92`.
  - No endpoint in the repo calls `RequireRateLimiting`. The be sdk's per-IP limiter is a named policy only, not global.
- **Defect:** each new code brings 5 fresh attempts, and a new code can be requested 5 s after the last one. Nothing caps failed verifications per number across codes.
- **Scenario:**
  - For any number already linked to the bot (every returning Ocharo user), the attacker loops: `phone-sign-in/send`, then 5 guesses at `phone-sign-in`, then a 5 s wait.
  - That is about 17k codes a day: 1 − (1 − 5·10⁻⁶)^17280 ≈ 8 % per day, and about 45 % per week, for a full account takeover.
  - The victim gets a Telegram message every 5 s.
  - Blocking the bot does not stop it: `SendCodeAsync` creates the code before delivery fails (`:92` runs before `:99`).
- **Fix:**
  - Add a per-subject failure budget across codes (for example 10 failures per hour, then refuse to issue codes for N hours).
  - Raise the default `RateLimitWindow` to 30 – 60 s.
  - Put a `RateLimits` policy on `/phone-sign-in*` and `/sign-in`.
  - Do not create a code when delivery fails.

---

### Medium

#### 3. Unauthenticated sign-in requests can exhaust memory: 19 MiB of Argon2 each, no rate limit, 256 MB container — PLAUSIBLE

- **Where:**
  - `BE/Module/PasswordHashing/Argon2/Argon2PasswordHasher.cs:15-17` (m = 19 456 KiB, t = 4).
  - `BE/Module/Core/SignIn/SignInService.cs:109,439-441`: unknown identifiers also hash, through the decoy.
  - `deployment/deploy.yml:65` (`memory: 256m`).
- **Defect:** every `POST /sign-in` runs a 19 MiB, ~140 ms Konscious Argon2 on a thread-pool thread. Nothing limits concurrency or rate.
- **Measured** (scratchpad probe, macOS, Release): 12 concurrent hashes peaked at 498 MiB working set, from a 37 MiB baseline.
- **Scenario:** about 6 – 10 parallel sign-in posts push the container past its limit. That ends in an OOM kill or `OutOfMemoryException` 500s, so every user's sign-in fails while the attack runs. The attacker needs no account.
- **Fix:**
  - Bound concurrent hashes with a `SemaphoreSlim` (2 – 4) in the hasher, and queue or 429 the rest.
  - Add a per-IP rate limit on the password routes.
  - Size the container for the hash cost.

#### 4. BFF token refresh runs on the browser's `RequestAborted`, so a rotated refresh token can be lost — CONFIRMED (mechanism; race not reproduced)

- **Where:** `BE/Client/Bff/Sessions/BffCookieEvents.cs:40`, `BffSessionService.cs:202,230` and `BffTokenClient.cs:48,51,59`.
- **Defect:** the refresh POST, the read of its body and the save of the new token all take `context.HttpContext.RequestAborted`.
- **Scenario:**
  - An Ocharo user reloads or closes the tab while a refresh is in flight. Identity has already redeemed RT0, but RT1 is never saved.
  - Their next visit presents RT0 again. That is after OpenIddict's 30 s reuse leeway, so it revokes the whole chain, answers 400, and the BFF ends the session.
  - A 5xx or timeout after Identity committed leaves the same spent RT0 behind (classed `Unavailable`).
- **Fix:** run the refresh and the save on `CancellationToken.None` with their own timeout. Optionally retry `Unavailable` within the leeway.

#### 5. With the BFF defaults, every Ocharo restart or deploy signs out every user — CONFIRMED (defaults)

- **Where:** `BE/Client/Bff/IdentityBffServiceCollectionExtensions.cs:53-54` (`AddDataProtection()` with no key store, `AddDistributedMemoryCache()`) and `BE/Client/README.md:12`. The quick start shows only `AddIdentityBff()`.
- **Defect:** the session store is in-process memory, and the Data Protection keys live in the container's home folder. Both vanish on container replacement.
- **Scenario:**
  - Each dev auto-deploy invalidates every `__Host-` BFF cookie, CSRF token and in-flight sign-in ("Correlation failed").
  - Every API call answers 401 until the user signs in again, so the 7 – 30 day sessions never survive a release.
- **Fix:**
  - Document a persistent `IDistributedCache` and a key ring as required.
  - Better: warn or refuse at boot outside a local run when the in-memory cache or an unpersisted key ring is in use.

#### 6. Concurrent wrong passwords lose lockout increments — CONFIRMED (traced)

- **Where:**
  - `BE/Module/Core/Lockout/UserLockoutService.cs:38-47`: read, modify, then save the user row.
  - `BE/Module/Core/EfUserRepository.cs:58-59` and `IdentityDbContext.cs:157`: `ConcurrencyStamp` is a concurrency token.
  - be sdk `DbExceptionMappingRule` maps the concurrency failure to 409.
- **Defect:** K parallel guesses all load the same stamp during the ~140 ms hash. Only the first save wins, and the rest throw `DbUpdateConcurrencyException`, which answers 409. A wrong password is evident either way, but the counter rises by 1 per batch, not by K.
- **Scenario:**
  - An attacker gets about 5·K guesses per 5-minute lockout window instead of 5.
  - A legitimate double submit of a sign-in, with failed attempts already recorded, gets a spurious 409.
- **Fix:** count attempts with a conditional `ExecuteUpdateAsync` (`access_failed_count = access_failed_count + 1 … RETURNING`) and lock in the same statement. Do not let a lockout write fail the request.

#### 7. After any emailed link, the person lands on the Identity console, not back in Ocharo — CONFIRMED

- **Where:**
  - `FE/apps/web/src/presentation/account/pages/ConfirmEmailPage.vue:18`, `ResetPasswordPage.vue:17` and `ChangePasswordPage.vue:22` use a bare `HostedPageRouteConstants.signIn`.
  - The email links carry no return URL: `service/Identity.Api/Configurations/HostConfiguration.Extensions.cs:131-132,137`.
  - `FE/apps/web/src/application/account/ReadAccountRequests.ts:30` falls back to `/`, which is the console.
- **Scenario:** an Ocharo user confirms their email or resets their password, signs in, and is shown "Identity console" with version badges. They have no path back to the app.
- **Fix:**
  - Carry the return URL through the emails, as a `{returnUrl}` placeholder validated with `readLocalPath`, or stored with the token.
  - At minimum, show a "signed in, return to the app" page instead of the console.

---

### Low

#### 8. Phone numbers with an Ocharo account can be enumerated — CONFIRMED

- **Where:**
  - `BE/Module/Core/Passwords/UserPasswordService.cs:164-165`: a null hash fails without hashing.
  - `BE/Module/Core/SignIn/SignInService.cs:106-110`: only an unknown identifier spends the decoy.
  - `BE/Module/Core/PhoneSignIn/PhoneSignInEndpointRouteBuilderExtensions.cs:58-59`: `isCodeSent` true or false reveals whether a number is linked. The doc at `:33` says "Known and unknown numbers answer alike".
- **Scenario:** `POST /sign-in {identifier:"+998…"}` answers about 140 ms faster for an existing phone user than for an unknown number. `phone-sign-in/send` also tells linked numbers apart. Either way the attacker gets a target list for #1 and #2.
- **Fix:** spend the decoy for passwordless accounts (the same change as #1), and answer `send` identically for both cases, or accept the leak and correct the doc.

#### 9. `admin create --email` adopts a self-signed-up, unconfirmed account as the owner — CONFIRMED

- **Where:** `BE/Module/Core/Admin/FirstOwnerService.cs:127,137`. `FindByEmailAsync` does not check `IsEmailConfirmed`. Phone matching does require a confirmed number (`:128`).
- **Scenario:** sign-up is on, as in compose or if Ocharo enables it. Before the operator runs `admin create --email boss@ocharo.uz`, an attacker signs up with that email and their own password. The run reports `created`, and the attacker is the organization's owner until the real owner resets the password.
- **Fix:** refuse, or require `--user-id`, when the email match is unconfirmed or has a password the operator did not set.

#### 10. `prompt=login` and `max_age` are not enforced — CONFIRMED

- **Where:** `BE/Server/OidcServer/OidcServerEndpointRouteBuilderExtensions.cs:29,255-256` strip both from the `returnUrl`. `:58` takes `auth_time` from the cookie's `IssuedUtc`, which the sliding reissue resets in `BE/Module/Core/Sessions/SessionIdentityBuilderExtensions.cs:76`.
- **Scenario:**
  - An app asks for a step-up. The person, or someone at their unlocked browser, opens the `returnUrl` without signing in, and the old cookie satisfies the stripped authorize request.
  - Separately, a 6-day-old sliding session reports an `auth_time` from minutes ago.
- **Fix:**
  - Keep a request-scoped "authenticated after" marker in the `returnUrl` (or a short-lived cookie) and check it on return.
  - Carry the real authentication time in a claim or the session row's `CreatedAt`.

#### 11. Any site can sign the person out of Identity and every app (logout CSRF) — CONFIRMED

- **Where:** `BE/Server/OidcServer/OidcServerEndpointRouteBuilderExtensions.cs:47,168-183`. A bare `GET /connect/endsession` revokes the cookie's session with no `id_token_hint`.
- **Scenario:** a top-level navigation from any site sends the `Lax` cookie. The session and every grant under its `sid` are revoked, so Ocharo's BFF session dies at its next refresh.
- **Fix:** revoke only with a valid `id_token_hint`. Otherwise show a confirmation page on GET and revoke on a same-origin POST.

#### 12. First phone sign-in creates the person, then answers 403 forever, when `IsConfirmedEmailRequired` is on — CONFIRMED (config-dependent)

- **Where:** `BE/Module/Core/PhoneSignIn/UserPhoneSignInService.cs:142-145` creates the user with no email. `BE/Module/Core/SignIn/SignInService.cs:396` then refuses them.
- **Scenario:** an instance that requires confirmed email for its password users locks out every phone user, and each first attempt leaves an orphan account.
- **Fix:** satisfy the preconditions per method (a phone sign-in proves the phone), or refuse before creating the person.

#### 13. Sign-up with a malformed email, or a long user name, creates the account and then answers 500 — CONFIRMED

- **Where:** `BE/Module/Core/Endpoints/UserAccountEndpointRouteBuilderExtensions.cs:120-125`, which never validates `Email` or `UserName`.
- **Mechanism:**
  - be sdk `MailKitEmailBroker.cs:40` builds the MIME message outside its `try` (`:42`). A probe confirmed MimeKit throws `ParseException` for `"a b@c.com"`.
  - A `UserName` over 256 characters, or an email over 320, raises PostgreSQL error 22001. `DbExceptionMappingRule` does not map it, so it becomes a 500.
- **Scenario:** a client posts `email:"a b@c.com"` with sign-up on. The account is stored and the response is 500. A retry answers `DuplicateEmail`.
- **Fix:** validate the address shape and the length limits in `SignUpAsync` (400), and move `BuildMime` inside the `try`.

#### 14. BFF checks refreshed ID tokens against signing keys it never refreshes — CONFIRMED (traced)

- **Where:** `BE/Client/Bff/Sessions/BffTokenClient.cs:137-144` never sets `ConfigurationManager` and never calls `RequestRefresh`.
- **Scenario:** within 12 h of a new signing key going live (`keys generate` without `--not-before`, or the compromise runbook), every refreshing session gets a key-not-found failure, classed `Rejected`, and is signed out.
- **Fix:** set `parameters.ConfigurationManager`, or on `SecurityTokenSignatureKeyNotFoundException` call `RequestRefresh()` and retry once.

#### 15. A persistent callback failure becomes a redirect loop — CONFIRMED for page routes, PLAUSIBLE for the SPA

- **Where:** `BE/Client/Bff/Sessions/BffOpenIdConnectEvents.cs:31` redirects back to the original URI with no failure marker.
- **Scenario:** with a wrong `ClientSecret` or clock skew, a `RequireAuthorization()` page cycles through challenge, authorize and failed callback until ERR_TOO_MANY_REDIRECTS. Each pass creates an authorization row.
- **Fix:** redirect with a failure marker that the sign-in route and the SPA check before challenging again.

#### 16. A superseded phone code becomes valid again once the newer code is used — CONFIRMED

- **Where:** `BE/Module/Core/OneTimeCodes/EfOtpRepository.cs:63-67` and `BE/Module/Otp/OtpService.cs:105`. Verification takes the latest *unconsumed* code, and creating a new code does not retire the old one.
- **Scenario:** the user taps "send a new code" and signs in with C2. C1, still in the Telegram chat or a notification preview, signs in again for the rest of its 5 minutes.
- **Fix:** on create, mark every pending code for the subject and scope consumed; or on success, consume all pending codes.

#### 17. The FE says something was sent when nothing was — CONFIRMED

- **Where:** `FE/packages/fe-identity/src/kit/presentation/forms/passwordSignIn/PasswordSignIn.vue:63-64,73-75` and `phoneCodeSignIn/PhoneCodeSignIn.vue:101-108`.
- **Defect:**
  - The email resend button shows on any 403, but `SignInNotAllowed` also covers an unconfirmed phone (`SignInService.cs:395-397`), and the backend's resend always answers 204.
  - "Send a new code" reports "We sent a new code" even when the server returned a fresh Telegram link (`isCodeSent:false`).
- **Scenario:** the user waits for an email or code that never comes.
- **Fix:**
  - Return distinct `UnconfirmedEmail` and `UnconfirmedPhone` codes, and gate the button on them.
  - Go back to the Telegram step when `!isCodeSent && telegramLinkUrl`.

#### 18. The FE ignores the declared code length, so typos burn OTP attempts — CONFIRMED

- **Where:** `FE/packages/fe-identity/src/kit/presentation/forms/phoneCodeSignIn/PhoneCodeSignIn.vue:89-90` and `FE/apps/web/…/SignInOptionsApi.ts:16-20`. Both drop the `Length` rule that `SignInDeclarationBuilder.cs:142` declares.
- **Scenario:** a 5-digit typo spends 1 of 5 attempts. After a few typos the valid code is locked (`MaxAttemptsReached`).
- **Fix:** pass the declared code rules into `PhoneCodeSignIn` and validate before submitting.

#### 19. The kit has no second-factor step — CONFIRMED (dormant on the hosted service)

- **Where:** `PasswordSignIn.vue:59-66` and `PhoneCodeSignIn.vue:89-97` never read the `TwoFactorRequired` problem. The backend sends it at `UserAccountEndpointRouteBuilderExtensions.cs:145` and `PhoneSignInEndpointRouteBuilderExtensions.cs:91`.
- **Scenario:** the hosted service does not register `.AddTwoFactor()`, so only a user row with `IsTwoFactorEnabled` set can hit this. That user sees "Enter your two-factor code." with no field. A Form 1 host that registers the slice is fully affected.
- **Fix:** add a code or recovery-code step that posts `sign-in/two-factor` with `userId` + `twoFactorTicket`.
