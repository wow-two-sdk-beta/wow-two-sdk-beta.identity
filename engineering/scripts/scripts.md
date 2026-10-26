# scripts/

Release helpers `publish-packages.yml` calls; each runs from anywhere in the repository.

| Script | Does |
|---|---|
| `next-version.sh be\|fe` | prints the next version: `10.y.z-beta` from the newest `be-v*` tag; `0.0.z` from the higher of the newest `fe-v*` tag and npm |
| `test-next-version.sh` | tests the version logic in throwaway repositories (CI: the delivery workflow's release-scripts check) |
| `verify-release-packages.sh <dir> <version> <commit>` | one nupkg and snupkg per `packages/*` project: id, version, MIT, README, commit, assembly; be sdk floor = the `BackendSdkVersion` pin; family at one version |
| `verify-published-packages.sh <version>` | polls nuget.org until every id shows the version |
| `pack-smoke.sh <dir> <version>` | restores the packed family in a throwaway consumer and compiles both forms and the test kit |

## Releases

| Artifact | Line | Tag | Workflow |
|---|---|---|---|
| NuGet family | `10.y.z-beta` | `be-v{version}` | `publish-packages.yml`, job `be` |
| `@wow-two-beta/fe-identity` | `0.0.z` | `fe-v{version}` | `publish-packages.yml`, job `fe` |
| `identity` image | `X.Y.Z` | `vX.Y.Z` | `publish-docker-image.yml` |

- A job runs only when its sources changed since its newest tag; the tag is pushed before the publish.
- No release commit, no GitHub Release for `be-v` / `fe-v`; the notes are the commit log between tags.
- Arming, once per repo: nuget.org trusted-publishing policy for `publish-packages.yml`, then the repository variable
  `PUBLISH_PACKAGES=true`. npm: publish `0.0.1` by hand and add the trusted publisher; `next-version.sh fe` reads npm, so no tag.
