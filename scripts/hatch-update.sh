#!/usr/bin/env bash
# Pull the trunk and rebuild the stack when origin has moved.
# Run from anywhere; it works on the checkout this script lives in.
set -euo pipefail
src=${BASH_SOURCE[0]}
while [ -L "$src" ]; do
  dir=$(cd -P "$(dirname "$src")" && pwd)
  src=$(readlink "$src")
  case $src in /*) ;; *) src=$dir/$src ;; esac
done
cd -P "$(dirname "$src")/.."

branch=$(git symbolic-ref --short refs/remotes/origin/HEAD 2>/dev/null || true)
branch=${branch#origin/}
branch=${branch:-main}

git fetch origin "$branch" --quiet
if [ "$(git rev-parse HEAD)" = "$(git rev-parse "origin/$branch")" ]; then
  echo "hatch: already up to date"
  exit 0
fi

# Only update a clean checkout on the trunk; never clobber local work.
[ "$(git branch --show-current)" = "$branch" ] || { echo "hatch: not on $branch, skipping"; exit 0; }
[ -z "$(git status --porcelain)" ] || { echo "hatch: working tree dirty, skipping"; exit 0; }

git pull --ff-only
docker compose "$@" up -d --build   # e.g. hatch-update.sh --profile runner
