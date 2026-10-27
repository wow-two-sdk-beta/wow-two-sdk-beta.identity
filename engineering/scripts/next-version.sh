#!/usr/bin/env bash
# Prints the next release version of one package line, from the repository's tags (and npm for fe). CI never commits a
# version: publish-packages.yml passes this one to the pack step, then pushes the tag before it publishes.
#
#   next-version.sh be   10.y.z-beta, one past the newest be-v10.* tag, z without a ceiling; first 10.0.0-beta
#   next-version.sh fe   0.0.z, one past the highest of the newest fe-v* tag and every version npm holds; first 0.0.1
#
# Env:
#   FE_PACKAGE     the npm name to ask the registry about (default: the name in packages/fe-*/package.json)
#   NPM_VERSIONS   newline-separated versions standing in for the registry (tests; skips the network)
set -euo pipefail

line=${1:?usage: next-version.sh be|fe}

newest() { # newest <prefix> <version regex>: the highest tagged version of a line, or nothing
  git tag --list "$1*" | sed "s/^$1//" | grep -E "$2" | sort -t. -k1,1n -k2,2n -k3,3n | tail -n 1 || true
}

case "$line" in
  be)
    latest=$(newest be-v '^10\.[0-9]+\.[0-9]+-beta$')
    if [ -z "$latest" ]; then
      echo "10.0.0-beta"
      exit 0
    fi
    IFS=. read -r x y z <<< "${latest%-beta}"
    echo "$x.$y.$((10#$z + 1))-beta"
    ;;
  fe)
    if [ -n "${NPM_VERSIONS+set}" ]; then
      registry=$NPM_VERSIONS
    else
      root=$(git rev-parse --show-toplevel)
      manifests=("$root"/engineering/codebase/*.frontend-services/packages/fe-*/package.json)
      package=${FE_PACKAGE:-$(node -p "require(process.argv[1]).name" "${manifests[0]}")}
      # A package npm has never seen answers E404; any other failure stops the release rather than guess low.
      if answer=$(npm view "$package" versions --json 2>/dev/null); then
        registry=$(node -e 'const v = JSON.parse(process.argv[1]); console.log([].concat(v).join("\n"))' "$answer")
      elif grep -q E404 <<< "$answer"; then
        registry=""
      else
        echo "npm view $package failed: $answer" >&2
        exit 1
      fi
    fi
    highest=$( { newest fe-v '^0\.0\.[0-9]+$'; printf '%s\n' "$registry"; } \
      | grep -E '^0\.0\.[0-9]+$' | sort -t. -k3,3n | tail -n 1 || true)
    if [ -z "$highest" ]; then
      echo "0.0.1"
    else
      echo "0.0.$(( 10#${highest##*.} + 1 ))"
    fi
    ;;
  *)
    echo "usage: next-version.sh be|fe" >&2
    exit 2
    ;;
esac
