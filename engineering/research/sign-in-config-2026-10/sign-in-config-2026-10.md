# Sign-in configuration — settings, declaration, roles, console access

*Last updated: 2026-10-31*

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
  `MaxLength`, `RequireDigit`, `RequireLowercase`, `RequireUppercase`, `RequireNonAlphanumeric`, `RequiredUniqueChars`,
  `RejectAccountIdentifiers`), but no settings section binds them, and `MinLength` is 8 against Q20's 15.
- A public declaration names the enabled methods, their fields and their rules; `fe-identity` renders forms and pages
  from it, the password field's live checklist included.
- Ocharo signs in by phone through its own Telegram bot: phone route, durable codes (I15), webhook and link (I20),
  phone pre-registration (I14).
- The console gets an instance-role gate, a host CLI `admin create`, and member management for organization owners.
- The fe sdk's `PhoneInput` takes a list of allowed countries; Identity's phone settings name them and the declaration
  carries them.

---

## 2 · Settings that change under existing accounts

| Change | Example | Reaction |
|---|---|---|
| invalid on its own | `MinLength` above `MaxLength`; Telegram on without a bot token; an unknown country code | refuse: `config check` exits 1, a deployed boot refuses to start |
| loosens a rule | password minimum 15 → 8; email verification off | applies at once |
| tightens a rule | password minimum 8 → 15; `RequireDigit` on; email verification or a second factor becomes required | starts; each person meets the rule at the next sign-in, as a required step before any token |
| removes a method | password sign-in off | starts; sessions and refresh tokens whose `authentication_methods` hold only that method are revoked, by a boot sweep and on each use |
| strands people | password off while some people have only a password; the bot replaced while people are linked only to the old one | `config check` refuses unless the settings accept it; a boot that meets it anyway starts, logs `Critical` and reports `degraded` |

- Tightening is a routine owner action, not an inconsistency. Refusing it would make a policy impossible to raise.
  NIST SP 800-63B and the common identity products apply a new password rule when a password is next set.
- A password is plain text only during sign-in, so that is when the instance tests it against the current rule. A
  password that fails signs the person in to a "set a new password" step only.
- Every reaction compares a person's state with the current settings, so none needs the previous settings.
- Sessions already record `authentication_methods` (`003-spine`), so the method sweep needs no schema change.

### The rule for every wow2 service

- Fail fast on settings that are invalid on their own. The deployed boot already refuses missing keys and ephemeral
  keys.
- Never fail on settings that conflict with stored data: start, apply the safe reaction, log `Critical` for what stays
  unresolved, and report it in `GET /api/system/status`.
- A crash loop on an identity instance locks out every person, including the admin who would fix it.
- The deploy is the gate instead. `config check` runs before the restart, as `migrate` does, so a refused change stops
  the deploy while the old release keeps serving. This needs a descriptor item beside `migrate:` in Wheelhouse
  (`unverified` whether one exists).

---

## 3 · Where the settings live

| Option | Source | Verdict |
|---|---|---|
| A · settings only | Wheelhouse's settings and vault render `appsettings.Target.json`; a change is a redeploy | works; no history inside the instance |
| B · a table as the source | rows seeded on the first deploy, then edited in the console | rejected: two sources per instance; Wheelhouse can't diff or promote them dev → prod; later deploy settings would be ignored; reverses Q51 |
| C · settings plus a boot snapshot | A, and each boot writes the effective settings, without secrets, to `identity_settings_revisions` with the release and a hash | pick |

- C gives the console a history and a diff (`MinLength` 8 → 15 at release 0.4.1) without a second source.
- No reaction in § 2 reads the snapshot. It serves audit, the console and the summary `config check` prints.
- Restarting one container takes seconds, so editing settings at runtime buys little.

---

## 4 · Roles

A fixed catalog in three layers. An app adds permissions, never roles.

| Layer | Role | Can | In an app token |
|---|---|---|---|
| instance | `admin` | everything in the console: clients, key status, settings, every organization and person, instance-role grants | never |
| instance | `support` | people only: find a person, revoke sessions, unlock, reset a second factor, resend an invite, read the sign-in log | never |
| instance | `auditor` | read only: people, organizations, the sign-in log, settings revisions | never |
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

- One public `GET /api/account/sign-in-options`: no sign-in, a short cache with an `ETag`. A later `?clientId=` lets a
  client narrow the methods (Desk requiring a second factor).
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
2. `config check` and the § 2 reactions: required steps at sign-in, the method sweep, the stranding count, status
   `degraded`.
3. `identity_settings_revisions` and the boot snapshot, in a new migration file.
4. The declaration endpoint.
5. Telegram for Ocharo: phone route, I15, I20, I14.
6. The `fe-identity` kit and the hosted pages.
7. Roles: seeded instance roles, `AddRoles()` wired, a policy per console area, `admin create` and the enrollment
   link.
8. Console pages; "Open console" in Wheelhouse, with the Wheelhouse chat.

---

## 8 · Points

- [ ] C1 Settings changes: refuse only settings invalid on their own; tightening applies at the next sign-in; a removed method revokes its sessions; stranding is refused by `config check` before the restart, never by a crash loop → yes
- [ ] C2 Settings stay the only source; each boot snapshots them into `identity_settings_revisions` → yes
- [ ] C3 The role catalog of § 4, with wow2's `operator` renamed `editor` → yes
- [ ] C4 The console signs in by the instance's own cookie; Wheelhouse bootstraps and opens it through one-time links and keeps no password → yes
- [ ] C5 A declaration per method with fe sdk rule codes, and one `fe-identity` component per method; no schema renderer now → yes
- [ ] C6 Each rule carries its message in the request's locale → yes
- [ ] C7 A public `GET /api/account/sign-in-options`, with `?clientId=` narrowing later → yes
