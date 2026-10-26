# deployment/

How wow-two-sdk-beta.identity's service ships. Descriptor format: wow-two-ws/conventions/deployment/descriptor/deploy-descriptor.md.
The packages ship separately, from `publish-packages.yml` (`engineering/scripts/scripts.md`).

| File | Role |
|---|---|
| `deploy.yml` | descriptor 2: one service, `identity` (`/health`, 256 MB, the typed required key `DatabaseSettings:ConnectionString`, a `data` volume); sites `app` (public, primary) and `console` (private), both at `/` and `/api`; `dotnetEnvironment: true` and the `verify` dev check |
| `Dockerfile` | three stages: build the frontend workspace (`fe-identity`, then `apps/web`), publish `service/Identity.Api` with its packages and the pages in `wwwroot`, run on `aspnet` with `curl` |
| `docker-compose.yml` | local verification: the image with its SQLite file on a named volume, on `http://127.0.0.1:8389` |

## Contract with Wheelhouse

- Image: `ghcr.io/<owner>/<repo>/identity`, published by `.github/workflows/publish-docker-image.yml` through the shared
  `delivery.yml` of `wow-two-platform.pipelines` (pinned `v0.10.0`); CI never deploys. `v0.10.0` carries the typed
  setting keys (`settings.keys`) into the bundle; the runner needs Wheelhouse v0.5 or later.
- Version: `X.Y` from the newest `planning/version-track/v{X.Y}/`; the image tag and `GET /api/system/status` report the
  same `X.Y.Z`, plus the build's commit after a `+`. Package releases (`be-v*`, `fe-v*`) never move it.
- Settings a target provides in `/app/appsettings.Target.json`: `DatabaseSettings:ConnectionString`
  (`Data Source=/data/identity.db`); `Deployment:TrustedProxies` or `Deployment:TrustedNetworks` names the ingress.
- Database: SQLite on the `data` volume, one instance per environment; the host creates the module's identity schema
  on a new database before it serves (EF-owned until the module ships SQL migrations). `GET /health` answers 200 only while the database answers.
- Dev check: after a dev rollout, `verify` curls `$VERIFY_SERVICE_IDENTITY/health` and `/api/system/status` with
  `Host: localhost`; `HostFilteringTests` replays the descriptor's requests.
- After each main image publish, `conformance.yml` runs the contract suite against the released image.

## Local run

```bash
docker build --build-arg APP_VERSION=0.1.99 --build-arg VCS_REF=$(git rev-parse HEAD) \
  -f engineering/deployment/Dockerfile -t identity:local engineering/codebase
```
