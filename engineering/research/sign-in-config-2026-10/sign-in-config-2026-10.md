# Sign-in configuration — settings, declaration, roles, console access

*Last updated: 2026-11-01*

> How an instance turns sign-in methods, verification and policies on from settings, how the pages adapt, what an
> instance does when settings change under existing accounts, which roles exist, and how the console signs people in.
> Asked by the developer on 2026-10-31 and checked against the code that day. Ocharo's Telegram sign-in is the first
> configuration that must work.

## Inputs and limits

- Read: `packages/Module/Core/PasswordOptions.cs`, `Core/Passwords/PasswordRulesValidator.cs`,
  `Migrations/Postgres/003-spine/Apply.sql`, the account endpoints and option sections, the fe sdk's `PhoneInput`,
  and the hub's role model (`wow-two-ws/ideas/identity-instances-2026-10/` § Role model, People, Users).
- Not read: Wheelhouse's code. Whether a failed boot keeps the old release serving is `unverified`.

---

## 1 · Settled on 2026-10-31

- Console users are personal accounts gated by an instance role; no shared account.
- Every method, verification and policy comes from `Identity:*` settings (Q51, confirmed for the build).
- Password rules stay structured fields, never a regex. `PasswordOptions` already holds them (`MinLength`,
  `MaxLength`, `IsDigitRequired`, `IsLowercaseRequired`, `IsUppercaseRequired`, `IsNonAlphanumericRequired`,
  `RequiredUniqueChars`, `IsAccountIdentifierRejected`, named as § 8), but no settings section binds them, and `MinLength` is 8 against Q20's 15.
- A public declaration names the enabled methods, their fields and their rules; `fe-identity` renders forms and pages
  from it, the password field's live checklist included.
- Ocharo signs in by phone through its own Telegram bot: phone route, durable codes (I15), webhook and link (I20),
  phone pre-registration (I14).
- The console gets an instance-role gate, a host CLI `admin create`, and member management for organization owners.
- The fe sdk's `PhoneInput` takes a list of allowed countries; Identity's phone settings name them and the declaration
  carries them.

---

## 2 · Settings that change under existing accounts

Settled on 2026-10-31 (C1), revised on 2026-11-01 (C1b): a stricter rule is met at each person's next sign-in; only
settings invalid on their own and changes that strand people are refused.

| Change | Example | Reaction |
|---|---|---|
| invalid on its own | `MinLength` above `MaxLength`; Telegram on without a bot token; an unknown country code | the service refuses to start; `config check` exits 1 first |
| loosens a rule | password minimum 15 → 8; a country added to `Phone:AllowedCountries` | applies at once |
| tightens a password rule | password minimum 8 → 15; `IsDigitRequired` on | applies at the next sign-in: once the typed password matches its hash, it is checked against the current rules |
| tightens a sign-in requirement | email verification or a second factor becomes required | applies at once; each person meets it at the next sign-in, as a required step before any token |
| removes a method | password sign-in off | applies at once; sessions and refresh tokens whose `authentication_methods` hold only that method are revoked, by a boot sweep and on each use |
| strands people | password off while some people have only a password; a country removed while people sign in only by its phones; the bot replaced while people are linked only to the old one | `config check` exits 1; a deployed boot refuses to start |

- A refusing boot logs `Critical`: the setting, the count of people affected, and how to resolve it.
- Every reaction compares stored rows with the current settings, so none needs earlier settings.
- Sessions already record `authentication_methods` (`003-spine`), so the method sweep needs no schema change.

### A stricter password rule (C1b)

- A stored password is an Argon2id hash: it reveals neither its length nor its characters, and no such facts are
  stored beside it.
- At sign-in, once the typed password matches its hash, the instance checks it against the current rules.
- A failing password signs the person in to the change form only; no token is issued until it changes.
- The old hash stays until the change, so leaving the form never locks the person out.
- The change follows the C10 confirmation setting, as a change from the account security page does.

### Password change confirmation (C10)

| Setting | Values | Default |
|---|---|---|
| `Identity:Password:Change:Confirmation` | `PasswordChangeConfirmationType`: `None` · `Code` · `Link` | `None` |
| `Identity:Password:Change:IsSignInAgainRequired` | `true` · `false` | `false` |

- `None`: "Change password" opens the change form at once.
- `Code`: the instance sends a one-time code; the form takes it beside the new password.
- `Link`: the instance sends a one-time link; only that link opens the form.
- Both apply to the account security page and to the forced change of C1b.
- The API enforces the type: a change without a valid code or link token is refused.
- `Code` and `Link` name no channel: the change service injects `IOtpService` and `IOtpDeliveryHandlerFactory`.
  - The person's verified channel delivers it: email, Telegram or any later handler.
- A code or token is one use, 15 minutes, hashed at rest, scoped to the person and `password-change`.
- A link carries its token in the URL fragment, and the page spends it by `POST`. Mail scanners open links.
- `IsSignInAgainRequired` ends the current session too; otherwise it stays signed in.
- Every change ends the person's other sessions and notifies each verified channel.
- People with no verified channel under `Code` or `Link` count as stranded in `config check`.

### The rule for every wow2 service

- Refuse to start on settings invalid on their own. The deployed boot already refuses missing and ephemeral keys.
- Apply a stricter rule over stored rows at each row's next use, where the value can be checked; never refuse a start
  for it.
- Refuse a change that leaves people no way to use the service, at `config check` and at a deployed boot, logging
  `Critical`.
- `config check` is the boot's own validation, run without serving: read-only and stateless (C9). The deploy runs
  it after `migrate` and before the restart, so a refusal leaves the old release serving. Exit codes follow `migrate`:
  `0` consistent, `1` refused, `2` failed.
- Wheelhouse needs a descriptor item beside `migrate:` for it; the developer adds it (2026-10-31).
- Convention: `wow-two-ws/conventions/development/backend/dotnet/core/mla/components/settings.md` § Stored data.

### `config check` contract for Wheelhouse (asked 2026-11-01)

- Command: `[dotnet, Identity.Api.dll, config, check]`, in a one-off container with the service's settings, secrets
  included, as `migrate` runs.
- Database: read only, through `DatabaseSettings:ConnectionString` in a read-only transaction; it writes nothing.
- Order: after `migrate` and before the restart. A return to an older release runs that release's `config check` after
  its `migrate check`.
- Output: setting names, counts and the exit code only; never an email, phone number, name, id or secret value. Stdout
  ends with one `config-result` JSON line, as `migrate` ends with `migrate-result`.
- Exit codes: `0` consistent, `1` refused, `2` failed.
- No `config apply` exists: C1b moved every stricter rule to the next sign-in, so nothing resolves at deploy.

---

## 3 · Where the settings live

| Option | Source | Verdict |
|---|---|---|
| A · settings only | Wheelhouse's settings and vault render `appsettings.Target.json`; a change is a redeploy | pick (C2, 2026-11-01) |
| B · a table as the source | rows seeded on the first deploy, then edited in the console | rejected: two sources per instance; Wheelhouse can't diff or promote them dev → prod; later deploy settings would be ignored; reverses Q51 |
| C · settings plus a boot snapshot | A, and each boot writes the effective settings, without secrets, to `identity_settings_revisions` with the release and a hash | dropped: no reaction in § 2 reads earlier settings |

- Wheelhouse already audits each settings change (`EnvironmentSettingEntity` is `IAuditable`), so history and diffs
  live there.
- The one warning that needs earlier settings, a changed issuer ending every token, is Wheelhouse's to give: it
  renders the issuer from the site's URL.
- Restarting one container takes seconds, so editing settings at runtime buys little.

---

## 4 · Roles

A fixed catalog in three layers. An app adds permissions, never roles.

| Layer | Role | Can | In an app token |
|---|---|---|---|
| instance | `admin` | everything in the console: clients, key status, settings, every organization and person, instance-role grants | never |
| instance | `support` | people only: find a person, revoke sessions, unlock, reset a second factor, resend an invite, read the sign-in log | never |
| instance | `auditor` | read only: people, organizations, the sign-in log, the effective settings | never |
| organization | `owner` | members, roles, app access, the organization itself; the last-owner guard keeps one | `org_role` |
| organization | `admin` | members and app access below `admin`; never grants `owner` or `admin` (delegated admin, hub P3) | `org_role` |
| organization | `editor` | work in the apps it may reach, by each app's permission map | `org_role`, permissions |
| organization | `viewer` | read in the apps it may reach | `org_role`, permissions |
| organization | `billing` | the organization's subscription and invoices; no app access by default; built with billing | `org_role` |
| app | permissions | namespaced per app (`desk:edit`, `wheelhouse:deploy`), mapped from organization roles in the instance settings | `org_permission` |

- Each member holds one organization role (D3). A person may also hold instance roles.
- wow2's `operator` (hub § Role model) becomes `editor`. One vocabulary across instances lets the kit and the console
  label roles, and lets every permission map key on the same names.
- Ocharo's Desk roles (`owner`, `editor`, `viewer`) map unchanged.
- Left out on purpose: `guest` (customers hold no membership), per-app role overrides (the hub waits for a named need),
  custom roles (P3 at the earliest).
- Programs use client credentials with scopes, not roles.
- Granting `owner`, `admin` or any instance role takes a fresh passkey or TOTP step-up and writes an audit event (hub,
  T6 § 7).

---

## 5 · Console sign-in and Wheelhouse

- The console is the instance's own site, so it uses the instance's own cookie session. It registers no OIDC client
  for itself.
- Any instance role opens the console; each area checks its own role.
- A password that Wheelhouse keeps is the shared account again: a reusable secret in a second database, with no
  personal factor, and copied out of the dashboard.
- Wheelhouse's vault already holds the instance's signing keys, so a one-time link that Wheelhouse mints adds no trust
  it lacks today.

Proposed flow:

1. First deploy: Wheelhouse runs `admin create --email <owner> --role admin` (or `--phone`) in a one-off container, as
   it runs `migrate`.
2. The command prints a one-time enrollment link (one use, 15 minutes). Wheelhouse shows it to the owner once.
3. The owner opens it and enrolls their own factors: a passkey or TOTP, plus the Telegram code where phone sign-in is
   on.
4. Later, "Open console" in Wheelhouse mints a one-time sign-in link for the Wheelhouse person's own account in that
   instance. The console still asks for that person's second factor.
5. Wheelhouse keeps nothing reusable: no password and no long-lived token.

- Step 4 needs either Wheelhouse running a command on demand, or an instance management key
  (`Identity:Management:Keys:0`, secret) for a link endpoint. The choice is open with the Wheelhouse chat.
- Once wow2's shared instance signs people in to Wheelhouse, step 4 can become single sign-on for wow2's own
  instances. Ocharo's instance keeps its own accounts.

---

## 6 · The declaration and the kit

Shape, as a draft (rule codes illustrative; the fe sdk's `ValidationCodes` names win):

```json
{
  "signUp": false,
  "locales": ["uz", "ru", "en"],
  "methods": [
    {
      "id": "phone-code",
      "channels": ["telegram"],
      "fields": [
        { "name": "phone", "kind": "phone", "countries": ["UZ"],
          "rules": [{ "code": "required", "message": "…" }] },
        { "name": "code", "kind": "pin",
          "rules": [{ "code": "length", "params": { "length": 6 }, "message": "…" }] }
      ]
    },
    {
      "id": "password",
      "fields": [
        { "name": "userName", "kind": "text", "rules": [{ "code": "required", "message": "…" }] },
        { "name": "password", "kind": "password",
          "rules": [{ "code": "minLength", "params": { "min": 15 }, "message": "…" }] }
      ]
    }
  ],
  "secondFactors": ["totp", "recovery-code"],
  "required": { "phoneVerified": true, "emailVerified": false, "secondFactor": false }
}
```

- One public `GET /api/account/sign-in-options`: no sign-in, a short cache with an `ETag`. `?clientId=` returns that
  app's methods, theme and background (Desk requiring a second factor; C7).
- Each rule is a fe sdk `ValidationCodes` code with its params and its message in the request's locale. The browser's
  check and the server's answer then use the same text.
- `fe-identity` builds one component per method (`PhoneCodeSignIn`, `PasswordSignIn`, …), each rendering its fields
  from the declaration. Pages lay the methods out in the declared order.
- No generic schema renderer now: the fe sdk allows only a future `schema-form` module, and it would read this same
  rule shape.
- The server validates every field again; the declaration only mirrors the rules.

---

## 7 · Build order

1. Settings: bind `Identity:Password`, `Lockout`, `SignIn`, `Methods:*`, `Phone:AllowedCountries`, and the
   verification and second-factor requirements; `MinLength` 15; validated on start.
2. `config check` and the § 2 reactions: the password check at sign-in, required steps, the method sweep, the
   stranding counts, the `Critical` refusal on a deployed boot.
3. The declaration endpoint.
4. Telegram for Ocharo: phone route, I15, I20, I14.
5. The `fe-identity` kit and the hosted pages.
6. Roles: seeded instance roles, `AddRoles()` wired, a policy per console area, `admin create` and the enrollment
   link.
7. Console pages; "Open console" in Wheelhouse, with the Wheelhouse chat.

---

## 8 · Boolean names

- The backend has 50 public booleans; 5 carry `Is` or `Has` (`IsLockedOut`, `HasPassword`).
- Options and settings keys hold most: `RequireDigit` → `IsDigitRequired`, `SignUp:Enabled` → `SignUp:IsEnabled`.
- `IsDigitsAllowed` would restrict characters; the current fields require them, so the name is `IsDigitRequired`.
- Entity flags `email_confirmed`, `phone_number_confirmed`, `two_factor_enabled`, `lockout_enabled` sit in the published
  `identity_users`: renamed in place while no database keeps data, by expand and contract after.
- Result flags `Success`, `Succeeded`, `Ok` become `IsSuccess`.
- `deploy.yml` keys `Identity:SignUp:Enabled` and `Identity:Server:Keys:Ephemeral` rename with Ocharo's `deploy.yml` and
  Wheelhouse's stored values.
- Copies of ASP.NET Core names (`SaveTokens`, `SlidingExpiration`, `ValidateLifetime`) rename on our side and map.
- The fe sdk already prefixes `is*`, `has*`, `can*`, `show*` (`frontend/core/lla/notation/naming/props.md`); the backend
  conventions have no rule, and the be sdk prefixes 47 of 330.
- Parked: the wow2 convention and its sweep (developer, 2026-10-31).

---

## 9 · Points

- [x] C1 Settings changes → settings invalid on their own stop the service; a stricter rule that invalidates stored rows fails the start with a log; `config check` lets Wheelhouse check first (developer, 2026-10-31)
- [x] C1b A stricter password rule → checked at the next sign-in once the password matches; a failing one leads to the change form and stays until changed (developer, 2026-11-01)
- [x] C9 `config check` stays: the boot's own validation run without serving, stateless, for invalid settings and stranding; the pattern for every wow2 service (developer, 2026-11-01)
- [x] C10 Password change confirmation → `None` by default, `Code` or `Link` over any verified channel; `IsSignInAgainRequired` off by default (developer, 2026-11-01)
- [x] C8 Identity takes `Is` names → renamed at once, columns in place; Ocharo dev's identity data is reset (developer, 2026-11-01)
- [x] C2 Settings stay the only source → no snapshot table and no per-password revision; Wheelhouse audits settings changes (developer, 2026-11-01)
- [x] C3 The role catalog of § 4, with wow2's `operator` renamed `editor` (developer, 2026-11-01)
- [ ] C4 The console signs in by the instance's own cookie; Wheelhouse bootstraps and opens it through one-time links and keeps no password → yes
- [x] C5 A declaration per method with fe sdk rule codes, and one `fe-identity` component per method; no schema renderer now (developer, 2026-11-01)
- [x] C6 Each rule carries its message in the request's locale (developer, 2026-11-01)
- [x] C7 A public `GET /api/account/sign-in-options`, with `?clientId=` now for each app's methods, theme and background (developer, 2026-11-01)
