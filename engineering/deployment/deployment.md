# deployment/

How wow-two-sdk-beta.identity's service ships. Descriptor format: wow-two-ws/conventions/deployment/descriptor/deploy-descriptor.md.
The packages ship separately, from `publish-packages.yml` (`engineering/scripts/scripts.md`).

| File | Role |
|---|---|
| `deploy.yml` | descriptor 2: the product's `name` and `description`; one service, `identity` (`/health`, 256 MB, `needs: [postgres]`, typed keys: `DatabaseSettings:ConnectionString` filled from the target's database, the OIDC issuer, the first signing and encryption certificates and the first ring certificate required as vault secrets, `Ephemeral` and `Identity:SignUp:Enabled` defaulting to `false`; no volume); sites `app` (public, primary) and `console` (private), both at `/` and `/api`; `dotnetEnvironment: true`, the `verify` dev check and one `migrate:` item |
| `Dockerfile` | three stages: build the frontend workspace (`fe-identity`, then `apps/web`), publish `service/Identity.Api` with its packages and the pages in `wwwroot`, run on `aspnet` with `curl` |
| `docker-compose.yml` | local verification: a PostgreSQL 18 service (its volume at `/var/lib/postgresql`, as 18 refuses the old `data` mount), `identity-keys` makes throwaway OIDC and ring certificates with the image's `keys generate` once, `identity-migrate` runs its `migrate` command, then the image serves on `http://127.0.0.1:8389`; every image runs under an init process |

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
| `Identity:Server:Keys:Ephemeral` | — | `false` by the descriptor's default; a deployed boot refuses `true` |
| `Identity:Server:Clients:N:{ClientId,ClientSecret,RedirectUris:M,PostLogoutRedirectUris:M,Audience,PermissionNamespace}` | per app | one confidential client; the secret ends in `Secret`, so the vault keeps it |
| `Identity:Server:{SignInPath,AuthorizationCodeLifetime,AccessTokenLifetime,IdentityTokenLifetime,RefreshTokenLifetime}` | — | defaults `/sign-in`, 60 s, 10 min, 5 min, 12 h |
| `Identity:Sessions:{Lifetime,ActivityInterval}` | — | defaults 12 h absolute (the cookie's), 5 min |
| `Identity:SignInLog:{RetentionDays,CleanupInterval}` | — | defaults 180 days, 1 h |
| `Identity:SignUp:Enabled` | — | `false` by the descriptor's default: `POST /api/account/register` answers `403` and creates nothing, and an admin adds every account; an environment that takes self sign-up sets `true` in Wheelhouse's console |

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
