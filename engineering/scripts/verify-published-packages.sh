#!/usr/bin/env bash
# Polls nuget.org until every package of the family shows at <version>; one deadline for the whole family.
#
#   verify-published-packages.sh <version> [timeout-seconds] [poll-seconds]
set -euo pipefail

version=${1:?usage: verify-published-packages.sh <version> [timeout-seconds] [poll-seconds]}
timeout=${2:-900}
poll=${3:-15}

root=$(git rev-parse --show-toplevel)
pending=()
for project in "$root"/engineering/codebase/*.backend-services/packages/*/*.csproj; do
  assembly=$(basename "$project" .csproj)
  pending+=("$(tr '[:upper:]' '[:lower:]' <<< "${assembly/WoW.Two.Sdk./WoW2.Sdk.}")")
done
deadline=$((SECONDS + timeout))

while [ "${#pending[@]}" -gt 0 ] && [ "$SECONDS" -lt "$deadline" ]; do
  missing=()
  for id in "${pending[@]}"; do
    url="https://api.nuget.org/v3-flatcontainer/$id/$version/$id.$version.nupkg"
    status=$(curl --silent --head --output /dev/null --write-out '%{http_code}' --max-time 20 "$url" || true)
    if [ "$status" = 200 ]; then echo "available: $id $version"; else missing+=("$id"); fi
  done
  [ "${#missing[@]}" -eq 0 ] && { echo "every package is published at $version"; exit 0; }
  pending=("${missing[@]}")
  echo "pending: ${pending[*]}"
  sleep "$poll"
done

echo "::error::not published after ${timeout}s: ${pending[*]}; rerun this check before another release" >&2
exit 1
