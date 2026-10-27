# deployment/

How wow-two-sdk-beta.identity's service ships. Descriptor format: wow-two-ws/conventions/deployment/descriptor/deploy-descriptor.md.
The packages ship separately, from `publish-packages.yml` (`engineering/scripts/scripts.md`).

| File | Role |
|---|---|
| `deploy.yml` | descriptor 2: one service, `identity` (`/health`, 256 MB, typed keys: `DatabaseSettings:ConnectionString`, the OIDC issuer and the first signing and encryption key files required, `Ephemeral` defaulting to `false`, the Data Protection ring's directory; a `data` volume); sites `app` (public, primary) and `console` (private), both at `/` and `/api`; `dotnetEnvironment: true` and the `verify` dev check |
| `Dockerfile` | three stages: build the frontend workspace (`fe-identity`, then `apps/web`), publish `service/Identity.Api` with its packages and the pages in `wwwroot`, run on `aspnet` with `curl` |
| `docker-compose.yml` | local verification: `identity-keys` makes throwaway OIDC certificates with the image's `keys generate` once and `identity-migrate` runs its `migrate` command, then the image serves its SQLite file on a named volume, on `http://127.0.0.1:8389` |

## Contract with Wheelhouse

- Image: `ghcr.io/<owner>/<repo>/identity`, published by `.github/workflows/publish-docker-image.yml` through the shared
  `delivery.yml` of `wow-two-platform.pipelines` (pinned `v0.10.0`); CI never deploys. `v0.10.0` carries the typed
  setting keys (`settings.keys`) into the bundle; the runner needs Wheelhouse v0.5 or later.
- Version: `X.Y` from the newest `planning/version-track/v{X.Y}/`; the image tag and `GET /api/system/status` report the
  same `X.Y.Z`, plus the build's commit after a `+`. Package releases (`be-v*`, `fe-v*`) never move it.
- Settings a target provides in `/app/appsettings.Target.json`: `DatabaseSettings:ConnectionString`
  (`Data Source=/data/identity.db`); `Deployment:TrustedProxies` or `Deployment:TrustedNetworks` names the ingress; the
  OIDC server's settings below.
- Database: SQLite on the `data` volume (`Identity:Database:Provider` is `Sqlite` in `appsettings.json`), one instance
  per environment. The schema is the Module's `identity` migration set: `dotnet Identity.Api.dll migrate` creates the
  database, applies the set and claims the schema as `service`, exiting `0` applied or current, `1` refused, `2` a
  script failed. A deployed boot (`DEPLOY_ENVIRONMENT` set) only verifies the level and refuses to start while the set
  is behind; a local run applies it at boot. `GET /health` answers 200 only while the database answers.
- Migrate step: descriptor 2 has no step that runs a command before the new image serves, so `deploy.yml` declares
  none, and the first deployment waits for one; a release that adds a migration needs `migrate` run first.
- Dev check: after a dev rollout, `verify` curls `$VERIFY_SERVICE_IDENTITY/health` and `/api/system/status` with
  `Host: localhost`; `HostFilteringTests` replays the descriptor's requests.
- After each main image publish, `conformance.yml` runs the contract suite against the released image.

## OIDC server settings and key files

| Key | Required | Holds |
|---|---|---|
| `Identity:Server:Issuer` | yes | the public site's origin, such as `https://<identity-host>/`; tokens carry it as `iss`; a deployed boot refuses to start without it |
| `Identity:Server:Keys:Signing:N` | `:0` | a signing certificate file inside the container; a rotation adds `:1` |
| `Identity:Server:Keys:Encryption:N` | `:0` | an encryption certificate file; an old one stays until its refresh tokens expire |
| `Identity:Server:Keys:Ephemeral` | — | `false` by the descriptor's default; a deployed boot refuses `true` |
| `Identity:Server:Clients:N:{ClientId,ClientSecret,RedirectUris:M,PostLogoutRedirectUris:M,Audience,PermissionNamespace}` | per app | one confidential client; the secret ends in `Secret`, so the vault keeps it |
| `Identity:Server:{SignInPath,AuthorizationCodeLifetime,AccessTokenLifetime,IdentityTokenLifetime,RefreshTokenLifetime}` | — | defaults `/sign-in`, 60 s, 10 min, 5 min, 12 h |
| `DataProtection:KeyDirectory` | — | the sign-in cookie's key ring, `/data/data-protection` by the descriptor's default; a deployed boot refuses none |

- Each key file is one PEM bundle (certificate, then its unencrypted PKCS#8 private key). Until Wheelhouse renders vault
  files, they sit on the `data` volume under `/data/keys/`: the folder `0700` and each file `0444`, owned by the image's
  `app` user, outside any repository.
- A deployed boot (`DEPLOY_ENVIRONMENT` set) refuses to start on a missing or unreadable file, a certificate without its
  private key, a role with no certificate valid now, or ephemeral keys; the log names each problem. A local run uses
  the ephemeral keys `appsettings.Development.json` turns on.
- The OIDC endpoints (`/connect/*`, `/.well-known/*`) answer HTTPS only: behind the ingress the trusted forwarded
  scheme makes them so, while the plain-HTTP compose check reaches `/health` but gets `ID2083` from them.
- The owner makes each file with the image itself; nothing generates keys in CI or at runtime:

```bash
docker run --rm <image> keys generate --role signing [--days N] [--not-before <yyyy-MM-ddTHH:mm:ssZ>] > signing-<yyyy-MM>.pem
```

## Key rotation

1. Publish: generate the next certificate with `--not-before` 24 h ahead, add its file as the next `Signing:N`, deploy;
   the JWKS lists both, so relying parties cache it before it signs.
2. Activate: on its `NotBefore` the new certificate signs every new token, without a deploy.
3. Retire: 24 h after activation, remove the old file and its key, deploy.
4. Encryption: the same steps, retiring the old certificate only after the refresh lifetime plus a day.

- Cadence: signing every 90 days (certificates default to 120 days), encryption every 180 (210 days).
- A leaked key skips the overlap: T6 § 3 *Compromise runbook*
  (`engineering/research/identity-analysis-2026-10/t6-hosting-security.md`).

## Local run

```bash
docker build --build-arg APP_VERSION=0.1.99 --build-arg VCS_REF=$(git rev-parse HEAD) \
  -f engineering/deployment/Dockerfile -t identity:local engineering/codebase
```
