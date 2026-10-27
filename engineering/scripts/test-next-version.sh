#!/usr/bin/env bash
# Tests next-version.sh in throwaway repositories: first versions, increments, no z ceiling, foreign tags ignored,
# and the fe line taking the higher of its tags and the registry.
set -euo pipefail

script="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/next-version.sh"
failures=0

expect() { # expect <description> <expected> <line> [tags...]; NPM_VERSIONS passes through
  local description=$1 expected=$2 line=$3
  shift 3
  local repo actual
  repo=$(mktemp -d)
  git -C "$repo" init -q
  git -C "$repo" -c user.name=test -c user.email=test@example.test commit -q --allow-empty -m init
  for tag in "$@"; do git -C "$repo" tag "$tag"; done
  actual=$(cd "$repo" && NPM_VERSIONS="${NPM_VERSIONS-}" bash "$script" "$line")
  rm -rf "$repo"
  if [ "$actual" = "$expected" ]; then
    echo "ok   $description: $actual"
  else
    echo "FAIL $description: expected $expected, got $actual"
    failures=$((failures + 1))
  fi
}

expect "be first release" 10.0.0-beta be
expect "be increments z" 10.0.4-beta be be-v10.0.3-beta
expect "be picks the numerically newest" 10.0.11-beta be be-v10.0.2-beta be-v10.0.10-beta be-v10.0.9-beta
expect "be keeps z past 99" 10.2.100-beta be be-v10.2.99-beta
expect "be picks the newest past 99" 10.0.101-beta be be-v10.0.99-beta be-v10.0.100-beta
expect "be ignores image and fe tags" 10.0.1-beta be be-v10.0.0-beta v0.9.7 fe-v0.0.40
NPM_VERSIONS="" expect "fe first release" 0.0.1 fe
NPM_VERSIONS="" expect "fe increments its tag" 0.0.8 fe fe-v0.0.7 be-v10.0.30-beta v0.0.99
NPM_VERSIONS=$'0.0.1\n0.0.12' expect "fe follows a higher registry" 0.0.13 fe fe-v0.0.7
NPM_VERSIONS=$'0.0.1\n0.0.3' expect "fe follows a higher tag" 0.0.10 fe fe-v0.0.9

[ "$failures" -eq 0 ] || { echo "$failures failing case(s)"; exit 1; }
echo "all next-version cases pass"
