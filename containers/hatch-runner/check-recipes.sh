#!/usr/bin/env bash
# Builds <base-image> plus every shipped recipe, in the order the renderer
# concatenates them, and runs each recipe's .check in the result.
set -euo pipefail

base=${1:?usage: check-recipes.sh <base-image> [platform]}
platform=${2:-}
recipes=$(cd "$(dirname "$0")/../.." && pwd)/src/Hatch.Contracts/recipes

shopt -s nullglob
ids=$(for f in "$recipes"/*.Dockerfile; do f=${f##*/}; echo "${f%.Dockerfile}"; done | LC_ALL=C sort)
[ -n "$ids" ] || { echo "no recipes"; exit 0; }

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
mkdir "$work/context"
{ echo "FROM $base"; for id in $ids; do cat "$recipes/$id.Dockerfile"; done; } > "$work/Dockerfile"

args=(); [ -z "$platform" ] || args=(--platform "$platform")
docker build ${args[@]+"${args[@]}"} --load -f "$work/Dockerfile" -t hatch-runner-recipes:check "$work/context"

for id in $ids; do
  echo "recipe: $id"
  docker run --rm ${args[@]+"${args[@]}"} --entrypoint sh hatch-runner-recipes:check -c "$(cat "$recipes/$id.check")"
done
