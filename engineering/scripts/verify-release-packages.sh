#!/usr/bin/env bash
# Verifies the packed NuGet family before anything is tagged or pushed: one nupkg and snupkg per packages/* project,
# each at the release version, MIT, README, the release commit, its assembly; family dependencies at the same version;
# every dependency on the be sdk at the core pin (the nuspec floor); the contract on the kernel alone, with no framework
# reference; test frameworks only in the Testing package.
#
#   verify-release-packages.sh <artifact-dir> <version> <commit>
set -euo pipefail

artifacts=${1:?usage: verify-release-packages.sh <artifact-dir> <version> <commit>}
version=${2:?pass the release version}
commit=${3:?pass the release commit}

root=$(git rev-parse --show-toplevel)
backend=("$root"/engineering/codebase/*.backend-services)
core=$(sed -n 's|.*<BackendSdkVersion>\(.*\)</BackendSdkVersion>.*|\1|p' "${backend[0]}/Directory.Packages.props")
[ -n "$core" ] || { echo "BackendSdkVersion not found in Directory.Packages.props" >&2; exit 1; }

fail() { echo "::error::$*" >&2; exit 1; }

shopt -s nullglob
projects=("${backend[0]}"/packages/*/*.csproj)
nupkgs=("$artifacts"/*.nupkg)
snupkgs=("$artifacts"/*.snupkg)
[ "${#projects[@]}" -gt 0 ] || fail "no packages/*/*.csproj found"
[ "${#nupkgs[@]}" -eq "${#projects[@]}" ] || fail "expected ${#projects[@]} nupkgs, found ${#nupkgs[@]}"
[ "${#snupkgs[@]}" -eq "${#projects[@]}" ] || fail "expected ${#projects[@]} snupkgs, found ${#snupkgs[@]}"

for project in "${projects[@]}"; do
  assembly=$(basename "$project" .csproj)
  id=${assembly/WoW.Two.Sdk./WoW2.Sdk.}
  nupkg="$artifacts/$id.$version.nupkg"
  [ -f "$nupkg" ] || fail "missing $id.$version.nupkg"
  [ -f "$artifacts/$id.$version.snupkg" ] || fail "missing $id.$version.snupkg"

  nuspec=$(unzip -p "$nupkg" '*.nuspec')
  files=$(unzip -Z1 "$nupkg")
  grep -Fq "<id>$id</id>" <<< "$nuspec" || fail "$id: wrong id"
  grep -Fq "<version>$version</version>" <<< "$nuspec" || fail "$id: not at $version"
  grep -Fq '<license type="expression">MIT</license>' <<< "$nuspec" || fail "$id: license is not MIT"
  grep -Fq "commit=\"$commit\"" <<< "$nuspec" || fail "$id: repository commit is not $commit"
  grep -Fxq 'README.md' <<< "$files" || fail "$id: README.md not packed"
  grep -Fxq "lib/net10.0/$assembly.dll" <<< "$files" || fail "$id: lib/net10.0/$assembly.dll not packed"

  sdk=$(grep -oE 'dependency id="WoW2\.Sdk\.Beta\.Be(\.Abstractions|\.Testing[A-Za-z.]*)?" version="[^"]+"' <<< "$nuspec" || true)
  [ -n "$sdk" ] || fail "$id: no be sdk dependency"
  if grep -v "version=\"$core\"" <<< "$sdk" | grep -q .; then fail "$id: be sdk floor is not the pin $core"; fi
  family=$(grep -oE 'dependency id="WoW2\.Sdk\.Beta\.Be\.[A-Za-z]+\.[A-Za-z.]+" version="[^"]+"' <<< "$nuspec" \
    | grep -v 'WoW2\.Sdk\.Beta\.Be\.Testing' || true)
  if [ -n "$family" ] && grep -v "version=\"$version\"" <<< "$family" | grep -q .; then
    fail "$id: a family dependency is not at $version"
  fi
  if [[ "$id" == *.Abstractions ]]; then
    others=$(grep -oE 'dependency id="[^"]+"' <<< "$nuspec" | grep -v 'id="WoW2.Sdk.Beta.Be.Abstractions"' || true)
    [ -z "$others" ] || fail "$id: the contract depends on more than the kernel: $others"
    if grep -q 'frameworkReference' <<< "$nuspec"; then fail "$id: the contract carries a framework reference"; fi
  fi
  if [[ "$id" != *.Testing ]] && grep -Eq 'dependency id="(xunit|Microsoft.NET.Test.Sdk|AwesomeAssertions)' <<< "$nuspec"; then
    fail "$id: a production package depends on a test framework"
  fi
  echo "verified $id $version (core $core)"
done
