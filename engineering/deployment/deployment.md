# deployment/

How wow-two-sdk-beta.identity's service ships. Descriptor format: wow-two-ws/conventions/deployment/descriptor/deploy-descriptor.md.
The packages ship separately, from `publish-packages.yml` (`engineering/scripts/scripts.md`).

| File | Role |
|---|---|
| `deploy.yml` | descriptor 2: the product's `name` and `description`; one service, `identity` (`/health`, 256 MB, `needs: [postgres]`, typed keys: `DatabaseSettings:ConnectionString` filled from the target's database, the OIDC issuer, the first signing and encryption certificates and the first ring certificate required as vault secrets, `Identity:Server:Keys:IsEphemeral` and `Identity:SignUp:IsEnabled` defaulting to `false`; no volume); sites `app` (public, primary) and `console` (private), both at `/` and `/api`; `dotnetEnvironment: true`, the `verify` dev check and one `migrate:` item |
| `Dockerfile` | three stages: build the frontend workspace (`fe-identity`, then `apps/web`), publish `service/Identity.Api` with its packages and the pages in `wwwroot`, run on `aspnet` with `curl` |
| `docker-compose.yml` | local verification: a PostgreSQL 18 service (its volume at `/var/lib/postgresql`, as 18 refuses the old `data` mount), Mailpit as the mail sink (`http://127.0.0.1:8025` shows every email), `identity-keys` makes throwaway OIDC and ring certificates with the image's `keys generate` and the purpose-token key once, `identity-migrate` runs its `migrate` command, then the image serves on `http://127.0.0.1:8389` with sign-up on and the sink as its SMTP relay; every image runs under an init process |

## Contract with Wheelhouse

- Image: `ghcr.io/<owner>/<repo>/identity`, published by `.github/workflows/publish-docker-image.yml` through the shared
  `delivery.yml` of `wow-two-platform.pipelines` (pinned `v0.13.0`, as is `codeql.yml`); CI never deploys. Every push
  and pull request runs the checks, `renovate/**` branches included, which publish nothing; the concurrency group queues
  (`queue: max`) and sets no `cancel-in-progress`, which GitHub refuses beside it. The bundle carries the typed setting
  keys (`settings.keys`); the runner needs Wheelhouse v0.5 or later.
- Version: `X.Y` from the newest `planning/version-track/v{X.Y}/`; the image tag and `GET /api/system/status` report the
  same `X.Y.Z`, plus the build's commit after a `+`. Package releases (`be-v*`, `fe-v*`) never move it.
- Settings a target provides in `/app/appsettings.Target.json`: `DatabaseSettings:ConnectionString`, which Wheelhouse
  fills from the target's own PostgreSQL database (`needs: [postgres]`); `Deployment:TrustedProxies` or
  `Deployment:TrustedNetworks` names the ingress; the OIDC server's and the ring's settings below.
- Database: PostgreSQL 18 only (SQLite is banned for products), one database per instance and environment; the service
  keeps nothing on a volume. The schema is the Module's `identity` migration set, run by the be sdk verb tree:
  `dotnet Identity.Api.dll migrate <verb>` answers `check · plan · apply · seed · sets · rollback · forget · repair ·
  baseline · claim · locks · unlock`, and a bare `migrate` runs `apply` then `seed`. It exits `0` done or current, `1`
  refused, `2` failed, `3` busy; `rollback`, `forget`, `repair`, `baseline`, `claim` and `unlock` need
  `--confirm <database>`; stdout ends with one `migrate-result` line. A deployed boot (`DEPLOY_ENVIRONMENT` set) only verifies the level and refuses to start
  while the set is behind; a local run applies it at boot. `GET /health` answers 200 only while the database answers.
- First deploy after the move from SQLite: the old `data` volume holds nothing the service reads any more (accounts,
  OIDC clients and the ring start empty in PostgreSQL); remove it once the new database serves.
- Migrate step: `deploy.yml` declares one `migrate:` item, `identity` running `[dotnet, Identity.Api.dll, migrate]`
  within `10m`. On every forward deploy Wheelhouse's runner runs it after the pull and before the service restarts, in a
  one-off container with the service's settings plus `Migrations:ConnectionString`, which it fills from the target's
  database and the be sdk verbs connect the set through; any failure stops the deploy before a restart. A return to an
  older release runs that release's `migrate check` first. The publish runs `migrate sets --json` offline (no network,
  no settings) and records the `identity` set and its level in the bundle.
- Placement: Ocharo's instance is a service of Ocharo's own `deploy.yml`, the image pinned by tag, its set in Ocharo's
  database; wow2's shared instance is a Wheelhouse product of its own. A product pinning it declares its keys as in
  [Pinning the instance](#pinning-the-instance).
- Dev check: after a dev rollout, `verify` curls `$VERIFY_SERVICE_IDENTITY/health` and `/api/system/status` with
  `Host: localhost`; `HostFilteringTests` replays the descriptor's requests.
- After each main image publish, `conformance.yml` runs the contract suite against the released image.

## OIDC server, ring and session settings

| Key | Required | Holds |
|---|---|---|
| `Identity:Server:Issuer` | yes | the public site's origin, such as `https://<identity-host>/`; tokens carry it as `iss`; a deployed boot refuses to start without it |
| `Identity:Server:Keys:Signing:N` | `:0`, secret | a signing certificate's PEM bundle; a rotation adds `:1` |
| `Identity:Server:Keys:Encryption:N` | `:0`, secret | an encryption certificate's PEM bundle; an old one stays until its refresh tokens expire |
| `Identity:DataProtection:Certificates:N` | `:0`, secret | the Data Protection ring's certificate bundle; the ring itself lives in the database, wrapped by it |
| `Identity:Server:Keys:IsEphemeral` | — | `false` by the descriptor's default; a deployed boot refuses `true` |
| `Identity:Server:Clients:N:{ClientId,ClientSecret,RedirectUris:M,PostLogoutRedirectUris:M,Audience,PermissionNamespace}` | per app | one confidential client; the secret ends in `Secret`, so the vault keeps it |
| `Identity:Server:{SignInPath,AuthorizationCodeLifetime,AccessTokenLifetime,IdentityTokenLifetime,RefreshTokenLifetime}` | — | defaults `/sign-in`, 60 s, 10 min, 5 min, 12 h |
| `Identity:Sessions:{Lifetime,ActivityInterval}` | — | defaults 12 h absolute (the cookie's), 5 min |
| `Identity:SignInLog:{RetentionDays,CleanupInterval}` | — | defaults 180 days, 1 h |
| `Identity:SignUp:IsEnabled` | — | `false` by the descriptor's default: `POST /api/account/register` answers `403` and creates nothing, and an admin adds every account; an environment that takes self sign-up sets `true` in Wheelhouse's console |

- Each certificate value is one PEM bundle (certificate, then its unencrypted PKCS#8 private key) from `keys generate`;
  `deploy.yml` declares the first of each `secret: true`, so the value lives in Wheelhouse's vault and its settings
  render carries it into `/app/appsettings.Target.json`. A rotation's `:1` is an owner key. A value may also be a file
  path inside the container; no target needs one.
- A deployed boot (`DEPLOY_ENVIRONMENT` set) refuses to start on an unreadable value, a certificate without its private
  key, a role with no certificate valid now, ephemeral keys, or a missing or unfit ring certificate; the log names each
  setting, never its key material. A local run uses the ephemeral keys `appsettings.Development.json` turns on and may
  keep the ring unwrapped.
- The OIDC endpoints (`/connect/*`, `/.well-known/*`) answer HTTPS only: behind the ingress the trusted forwarded
  scheme makes them so, while the plain-HTTP compose check reaches `/health` but gets `ID2083` from them.
- Each bundle comes from the image's own `keys generate`; nothing generates keys in CI or in the running service:

```bash
docker run --rm <image> keys generate --role signing|encryption|data-protection [--days N] [--not-before <yyyy-MM-ddTHH:mm:ssZ>]
```

- The bundle goes to standard output and the thumbprint and dates to standard error. Wheelhouse runs it for a key that
  declares it as `generate` (Wheelhouse v0.11, pipelines `v0.14.0`): the bundle goes straight into its vault, its
  expiry from the certificate, and no person or agent sees it. By hand, paste the bundle into the vault secret and keep
  no copy.

## Account, email and phone settings

The instance also reads the settings below. `deploy.yml` declares none of them yet (next section), so a target sets each
one as an owner key; a secret goes to the vault, never into a settings file or a message.

| Key | Required | Holds |
|---|---|---|
| `Identity:Tokens:SigningKey` | yes, secret | base64 of at least 32 bytes, or the path of a file holding it; signs the confirmation, reset and forced-password-change tokens, and every instance on one database needs the same value; a deployed boot refuses to start without it, and `Identity:Tokens:IsKeyEphemeral` (a throwaway key) is for a local run only |
| `Identity:Email:From` · `FromName` | with `Smtp:Host` | the sender of every account email and code: an address, and its display name |
| `Identity:Email:Smtp:{Host,Port,SecureSocket,Username}` | — | the SMTP relay; `SecureSocket` is `None`, `Auto`, `SslOnConnect`, `StartTls` or `StartTlsWhenAvailable`; with no `Host` the instance boots, but confirmation, reset and password-change emails are skipped and logged, never sent |
| `Identity:Email:Smtp:Password` | secret | the relay's password |
| `Identity:Password:{MinLength,MaxLength,IsDigitRequired,IsLowercaseRequired,IsUppercaseRequired,IsNonAlphanumericRequired,RequiredUniqueChars,IsAccountIdentifierRejected}` | — | the password rules; defaults `15`, `128`, `false`, `false`, `false`, `false`, `1`, `true`; a stored password that breaks a rule must change at its next sign-in |
| `Identity:Password:Change:{Confirmation,IsSignInAgainRequired,Lifetime,LinkTemplate,LinkSubject,LinkBody}` | — | the password change: `Confirmation` is `None` (default), `Code` or `Link`; `Code` and `Link` need an email the person confirmed and the relay above; `Link` needs `LinkTemplate` with `{token}`, which the service fills from the issuer's origin when unset |
| `Identity:SignIn:{IsConfirmedEmailRequired,IsConfirmedPhoneNumberRequired,IsEmailLoginAllowed}` | — | the sign-in preconditions; defaults `false`, `false`, `true` |
| `Identity:Lockout:{MaxFailedAttempts,DefaultLockout,IsEnabledForNewUsers}` | — | defaults `5`, 5 minutes, `true` |
| `Identity:Telegram:IsEnabled` | — | `false` by default; on, phone sign-in and the bot's webhook run and the three keys below are required |
| `Identity:Telegram:BotToken` | with the bot, secret | the bot's API token |
| `Identity:Telegram:BotUserName` | with the bot | the bot's user name without `@`; the start links read `https://t.me/<name>?start=…`, and a replaced bot starts with no linked phones |
| `Identity:Telegram:WebhookSecret` | with the bot, secret | 1–256 of `A-Z a-z 0-9 _ -`; Telegram sends it in `X-Telegram-Bot-Api-Secret-Token` and the webhook answers `401` otherwise |
| `Identity:Otp:HashKey` | with the bot, secret | base64 of at least 32 bytes; keys every stored one-time code; a deployed boot refuses the bot without it, and every instance on one database needs the same value |
| `Identity:Phone:AllowedCountries:N` | — | ISO 3166-1 alpha-2 codes such as `UZ`; empty allows every known country; an unknown code refuses the start |

- Telegram needs one manual step per environment: register the webhook (`https://<public host>/api/telegram/webhook`
  and the `WebhookSecret` as `secret_token`) with Telegram's `setWebhook`; the `/api` path already routes to the service.
- The emails' links (`/confirm-email`, `/reset-password`, `/change-password`) point at the hosted pages on the origin of
  `Identity:Server:Issuer`; `UserAccounts:Emails:{ConfirmationLink,ResetLink}` and `Identity:Password:Change:LinkTemplate`
  override them.
- Self sign-up (`Identity:SignUp:IsEnabled`) also gates the account a first phone sign-in creates.
- Key material for `Tokens:SigningKey` and `Otp:HashKey` must be standard base64 (`+`, `/`, `=`): Wheelhouse's
  `generate: { random: N }` writes base64url by default, which these settings reject.

### deploy.yml keys still to add

Not added yet; each joins `services.identity.settings.keys` when its feature goes live on a target:

| Key | Descriptor entry |
|---|---|
| `Identity:Tokens:SigningKey` | `{ secret: true, required: true, generate: { random: 32, encoding: hex } }` (64 hex characters decode as base64 to 48 bytes), or a `generate.command` that prints standard base64 |
| `Identity:Email:From` · `Identity:Email:FromName` · `Identity:Email:Smtp:Host` · `Identity:Email:Smtp:Username` | plain text keys, owner-set per environment |
| `Identity:Email:Smtp:Port` · `Identity:Email:Smtp:SecureSocket` | a number and a `choice` over the five modes |
| `Identity:Email:Smtp:Password` | `{ secret: true }` |
| `Identity:Password:Change:Confirmation` | `{ type: choice, values: [None, Code, Link], default: None }` |
| `Identity:Telegram:IsEnabled` | `{ type: boolean, default: false }` |
| `Identity:Telegram:BotToken` · `Identity:Telegram:WebhookSecret` | `{ secret: true }` (the webhook secret may `generate` 32 random bytes, `hex`) |
| `Identity:Telegram:BotUserName` | plain text, owner-set |
| `Identity:Otp:HashKey` | `{ secret: true, generate: { random: 32, encoding: hex } }` |
| `Identity:Phone:AllowedCountries:0` | plain text, owner-set per country |

- Until `Identity:Tokens:SigningKey` is set, a deployed boot of this image refuses to start with
  `Identity:Tokens:SigningKey is required`; set it as an owner key on each target before deploying this version.

## Pinning the instance

A product running its own instance declares the keys below on the pinned service; Wheelhouse makes every secret on the
first deploy of each environment, so dev and prod never share one (descriptor:
wow-two-ws/conventions/deployment/descriptor/deploy-descriptor.md § Generated and linked keys).

```yaml
services:
  identity:
    image: ghcr.io/wow-two-sdk-beta/wow-two-sdk-beta.identity/identity:0.3.6
    health: /health
    needs: [postgres]
    settings:
      keys:
        DatabaseSettings:ConnectionString: { required: true }
        Identity:Server:Issuer: { from: Sites:identity:Url }
        Identity:Server:Keys:Signing:0:
          { secret: true, required: true, generate: { command: [dotnet, Identity.Api.dll, keys, generate, --role, signing] } }
        Identity:Server:Keys:Encryption:0:
          { secret: true, required: true, generate: { command: [dotnet, Identity.Api.dll, keys, generate, --role, encryption] } }
        Identity:DataProtection:Certificates:0:
          { secret: true, required: true, generate: { command: [dotnet, Identity.Api.dll, keys, generate, --role, data-protection] } }
        Identity:Server:Clients:0:ClientId: { required: true }
        Identity:Server:Clients:0:ClientSecret: { secret: true, required: true, generate: { random: 32 } }
    sites:
      identity: { paths: [/, /api] }
  desk:                                  # the product's app, a confidential client
    settings:
      keys:
        Auth:ClientSecret: { from: Identity:Server:Clients:0:ClientSecret }
migrate:
  - service: identity
    command: [dotnet, Identity.Api.dll, migrate]
    timeout: 10m
sites:
  identity: {}                           # bound to its host per environment in Wheelhouse
```

- `Sites:identity:Url` is the site's origin, so the issuer follows the host Wheelhouse binds; after a host change,
  earlier tokens stop validating and users sign in again.
- A client's id, redirect URIs and audience are plain keys the owner sets once; only its secret is generated.
- The client app names its own keys; `Auth:ClientSecret` stands for whatever path it reads.

## Key rotation

1. Publish: generate the next certificate with `--not-before` 24 h ahead, add it as the next `Signing:N` secret,
   deploy; the JWKS lists both, so relying parties cache it before it signs.
2. Activate: on its `NotBefore` the new certificate signs every new token, without a deploy.
3. Retire: 24 h after activation, remove the old secret, deploy.
4. Encryption: the same steps, retiring the old certificate only after the refresh lifetime plus a day.
5. Ring: add the next `data-protection` certificate as `Certificates:1` and deploy; it wraps new ring keys, the old one
   still unwraps the old keys; remove the old one once the ring has rolled past them (the ring rolls every 90 days).

- Cadence: signing every 90 days (certificates default to 120 days), encryption every 180 (210 days).
- A leaked key skips the overlap: T6 § 3 *Compromise runbook*
  (`engineering/research/identity-analysis-2026-10/t6-hosting-security.md`).

## Local run

```bash
docker build --build-arg APP_VERSION=0.1.99 --build-arg VCS_REF=$(git rev-parse HEAD) \
  -f engineering/deployment/Dockerfile -t identity:local engineering/codebase
```

- `docker compose -f engineering/deployment/docker-compose.yml up` starts the stack; a local sign-up sends its
  confirmation email to Mailpit (`http://127.0.0.1:8025`), and nothing leaves the machine.
- The compose file leaves the Telegram bot off; a local phone sign-in needs a bot of your own with its token, user name
  and webhook secret in `appsettings.Local.json`, and a webhook Telegram can reach.
