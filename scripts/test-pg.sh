#!/usr/bin/env bash
#
# Run a command against a throwaway PostgreSQL.
#
#   ./scripts/test-pg.sh dotnet test ./src/Hatch.Api.Tests/Hatch.Api.Tests.csproj
#
# Starts a scratch cluster from the server binaries the `postgres` dependency in
# .hatch/dependencies.json installs, on a free port, with trust authentication
# on localhost only. The command runs with HATCH_TEST_DATABASE_URL pointing at
# it - the variable src/Hatch.Api.Tests/Hatch/HatchDatabase.cs demands - and the
# cluster is stopped and deleted when the command ends, whatever its status.
# The command's own exit status is this script's.
#
# It is what `make test-api-local-db` runs, for a place with no Docker socket (a
# project image with only the checkout mounted). Where Docker is there,
# `make test-api-db` serves the same tests from the compose `db` service.
#
# Postgres refuses to run as root, and a container runner is root: then the
# cluster is made and run as the `postgres` user the server package creates.

set -euo pipefail

if [ "$#" -eq 0 ]; then
  echo "usage: scripts/test-pg.sh <command> [args...]" >&2
  exit 2
fi

if ! command -v initdb >/dev/null 2>&1 || ! command -v pg_ctl >/dev/null 2>&1 || ! command -v pg_isready >/dev/null 2>&1; then
  echo "scripts/test-pg.sh needs the PostgreSQL server binaries (initdb, pg_ctl, pg_isready) on PATH: install the \`postgres\` dependency declared in .hatch/dependencies.json, or use \`make test-api-db\` where Docker is available." >&2
  exit 1
fi

# As root, every Postgres call goes through the postgres user; anywhere else it
# is the caller's own.
as_pg=()
if [ "$(id -u)" -eq 0 ]; then
  if ! id postgres >/dev/null 2>&1 || ! command -v runuser >/dev/null 2>&1; then
    echo "scripts/test-pg.sh is running as root and needs the \`postgres\` user and \`runuser\` to start a server as someone else." >&2
    exit 1
  fi
  as_pg=(runuser -u postgres --)
fi

dir=$(mktemp -d)
cleanup() {
  "${as_pg[@]}" pg_ctl -D "$dir/data" -m immediate -w stop >/dev/null 2>&1 || true
  rm -rf "$dir"
}
trap cleanup EXIT

# The postgres user must be able to enter the directory, and nobody else needs to.
if [ "${#as_pg[@]}" -gt 0 ]; then
  chown postgres "$dir"
  chmod 700 "$dir"
fi

# The first port from 55432 up that nothing is listening on.
port=55432
while (exec 3<>"/dev/tcp/127.0.0.1/$port") 2>/dev/null; do
  port=$((port + 1))
  if [ "$port" -gt 55532 ]; then
    echo "scripts/test-pg.sh found no free port between 55432 and 55532." >&2
    exit 1
  fi
done

"${as_pg[@]}" initdb -D "$dir/data" -U postgres --auth=trust --encoding=UTF8 --locale=C.UTF-8 >"$dir/initdb.log" 2>&1 \
  || { cat "$dir/initdb.log" >&2; exit 1; }

"${as_pg[@]}" pg_ctl -D "$dir/data" -l "$dir/server.log" -w \
  -o "-p $port -c listen_addresses=127.0.0.1 -c unix_socket_directories=$dir -c fsync=off" start >/dev/null \
  || { cat "$dir/server.log" >&2; exit 1; }

pg_isready -h 127.0.0.1 -p "$port" -t 30 >/dev/null

export HATCH_TEST_DATABASE_URL="Host=127.0.0.1;Port=$port;Database=hatch_test;Username=postgres"
echo "==> $(pg_ctl --version) on port $port, scratch cluster removed on exit" >&2

# Not `exec`: the trap has to run afterwards.
status=0
"$@" || status=$?
exit "$status"
