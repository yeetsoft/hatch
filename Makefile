up:
	docker compose up -d --build

db:
	docker compose up -d db

down:
	docker compose down

destroy:
	docker compose down -v

build:
	bash -c 'export NVM_DIR="$$HOME/.nvm"; [ -s "$$NVM_DIR/nvm.sh" ] && \. "$$NVM_DIR/nvm.sh"; dotnet build ./src/Hatch.Api/Hatch.Api.csproj'

run:
	dotnet run --project ./src/Hatch.Api/Hatch.Api.csproj

test: test-api test-hatch test-web

# Clamped like test-hatch below, and for the same reason: nobody is there to
# press ^C. A test that hangs past two minutes is killed and named, and leaves
# a dump of every thread under the test project's TestResults/ - a hang read
# off its stacks rather than guessed at from a quiet log (HA-171). The whole
# suite takes well under one.
API_TEST_CLAMP = --blame-hang-timeout 2m --blame-hang-dump-type mini

test-api:
	dotnet test ./src/Hatch.Api.Tests/Hatch.Api.Tests.csproj $(API_TEST_CLAMP)

# The CLI - `hatch`, which lives in src/Hatch.Cli. Its own target because it
# is what somebody editing the loop runs, and because it needs neither a
# database nor a node: a stub wire and a stub session are the whole fixture.
#
# Clamped, because an unattended session runs this and nobody is there to press
# ^C: a test that hangs past thirty seconds is killed and named, and a run past
# two minutes is aborted. The whole suite takes under one.
test-hatch:
	dotnet test ./src/Hatch.Cli.Tests/Hatch.Cli.Tests.csproj \
		--blame-hang-timeout 30s --blame-hang-dump-type none \
		-- RunConfiguration.TestSessionTimeout=120000

# The few tests that spawn a real `claude` session (src/Hatch.Cli.Tests/
# DeniedToolsProofTests.cs). Opt-in: they spend the caller's own usage and answer
# with what a live model chose to do, so `make test-hatch` skips them.
test-hatch-live:
	HATCH_LIVE_CLAUDE_TESTS=1 dotnet test ./src/Hatch.Cli.Tests/Hatch.Cli.Tests.csproj \
		--filter "FullyQualifiedName~DeniedToolsProofTests" \
		--blame-hang-timeout 300s --blame-hang-dump-type none

# The grok equivalent (src/Hatch.Cli.Tests/GrokDeniedToolsProofTests.cs) - a
# real `grok` session on the caller's own account, same opt-in reasoning.
test-hatch-live-grok:
	HATCH_LIVE_GROK_TESTS=1 dotnet test ./src/Hatch.Cli.Tests/Hatch.Cli.Tests.csproj \
		--filter "FullyQualifiedName~GrokDeniedToolsProofTests" \
		--blame-hang-timeout 300s --blame-hang-dump-type none

# A built CLI for this machine, so that `hatch.sh work` starts in milliseconds
# rather than spending a few seconds in `dotnet run` deciding whether to build
# first. It is an optimisation and not a requirement - hatch.sh falls back to
# the SDK, and says so if there is neither.
#
# Deliberately still a plain, fast, host-RID Release build. `go-to-work` asks to
# be restarted as a newer build when its own source changes, and hatch.sh's
# supervisor calls this to make one - at two in the morning, between increments.
# The shippable artifact is `publish-hatch` below, which is a different and much
# slower thing aimed at a different consumer.
# HATCH_BUILD_ARGS is for the caller that wants more of dotnet than this says,
# e.g. `make build-hatch HATCH_BUILD_ARGS=--no-incremental` to compile
# everything again whether or not MSBuild thinks it is current.
HATCH_BUILD_ARGS ?=

# Stamped with HEAD's own sha and commit count, computed with git rather than
# taken as a build arg: the API only ever gets these from a CI build arg, but
# this target's primary caller - scripts/hatch.sh's own supervisor, rebuilding
# between increments at 2am (CLAUDE.md "If the loop spawned you") - has no CI
# step to supply one, and it is already running inside a checkout that has a
# git to ask. A git failure (no git, not a repository) leaves both properties
# empty, which reads as dev/0 exactly like an unstamped `dotnet build` today -
# never the reason this fails.
build-hatch:
	dotnet build ./src/Hatch.Cli/Hatch.Cli.csproj --configuration Release \
		-p:SourceRevisionId=$(shell git rev-parse HEAD 2>/dev/null) \
		-p:HatchSequence=$(shell git rev-list --count HEAD 2>/dev/null) \
		$(HATCH_BUILD_ARGS)

# The artifact somebody else downloads: one file, no SDK on the far side, for
# every platform this house runs on. Self-contained because an operator taking
# Hatch for the first time has no .NET; single-file because the whole promise is
# "put it on your PATH"; trimmed because the alternative is 70MB of framework
# nobody calls.
#
# Each RID lands in artifacts/hatch/<rid>/, which is what AERIE-935's Runner
# page serves from.
HATCH_RIDS = win-x64 osx-arm64 osx-x64 linux-x64

publish-hatch:
	for rid in $(HATCH_RIDS); do \
		echo "==> $$rid"; \
		dotnet publish ./src/Hatch.Cli/Hatch.Cli.csproj \
			--runtime $$rid \
			--configuration Release \
			--output ./artifacts/hatch/$$rid \
			-p:PublishSingleFile=true \
			-p:SelfContained=true \
			-p:PublishTrimmed=true \
			-p:SourceRevisionId=$(shell git rev-parse HEAD 2>/dev/null) \
			-p:HatchSequence=$(shell git rev-list --count HEAD 2>/dev/null) || exit 1; \
	done

# The same suite with the claim's tests turned on. They need a real Postgres and
# skip loudly without one (src/Hatch.Api.Tests/Hatch/HatchDatabase.cs): the claim
# is built out of conditional UPDATEs, and EF's in-memory provider cannot execute
# one at all - so `make test-api` alone can be green having verified none of
# them, and CI runs this lane rather than that one.
#
# The restore-drill test (BackupRestoreDrillTests) also needs pg_dump and psql of
# the server's major (18) on PATH, else it skips with a message saying why.
# Install postgresql-client-18 from PGDG
# (https://www.postgresql.org/download/linux/ubuntu/ on Ubuntu/Debian), Homebrew
# postgresql@18 on macOS, or the EDB/PGDG installer on Windows.
#
# The database named here is dropped and recreated. It is deliberately not the
# `hatch` database `make db` serves the app from, and nothing discovers a
# connection string on its own for the same reason: a test that TRUNCATEs what
# it finds should have been told exactly what to find.
test-api-db: db
	HATCH_TEST_DATABASE_URL="Host=localhost;Port=5432;Database=hatch_test;Username=user;Password=password" \
		dotnet test ./src/Hatch.Api.Tests/Hatch.Api.Tests.csproj $(API_TEST_CLAMP)

# Installs what the e2e cases in src/Hatch.Api.Tests need to drive a real
# browser: Chromium and WebKit, into Playwright's own per-user cache
# (%LOCALAPPDATA%\ms-playwright on Windows), not this checkout. WebKit is
# installed for HA-1047 (the phone layout); Firefox is not (HA-1030). Once per
# machine, and again whenever the Microsoft.Playwright package moves - the
# installer is the one that ships inside it, copied beside the test binary by
# the package's own build/Microsoft.Playwright.targets.
browsers:
	bash -c 'export NVM_DIR="$$HOME/.nvm"; [ -s "$$NVM_DIR/nvm.sh" ] && \. "$$NVM_DIR/nvm.sh"; \
	command -v pwsh >/dev/null || { echo "make browsers needs PowerShell (pwsh): Playwright'\''s installer is a PowerShell script. https://aka.ms/powershell" >&2; exit 1; }; \
	dotnet build ./src/Hatch.Api.Tests/Hatch.Api.Tests.csproj && \
	if [ "$$(uname -s)" = "Linux" ]; then \
		pwsh src/Hatch.Api.Tests/bin/Debug/net10.0/playwright.ps1 install chromium webkit --with-deps; \
	else \
		pwsh src/Hatch.Api.Tests/bin/Debug/net10.0/playwright.ps1 install chromium webkit; \
	fi'

# The browser cases alone, from nothing: builds the hatch bundle the board smoke
# (src/Hatch.Api.Tests/E2e/BoardRendersTests.cs) serves out of
# src/Hatch.Api/wwwroot/apps/hatch - gitignored, so a fresh checkout or worktree
# has none, and the smoke fails by design without one - then runs only
# Hatch.Api.Tests.E2e. It proves a real browser can load the board against the
# real API. `make test-api-db` runs these among everything else; this is the
# short loop for them.
#
# Uses test-api-db's database, so `make db` first (the dependency does it) and
# `make browsers` once per machine; each process makes its own scratch databases
# beside it, so two runs at once do not collide. $(API_TEST_CLAMP) is the same
# hang clamp. Node comes from nvm when nvm is the sourced shell function, else
# whatever is on PATH, which must be 22+ (src/Hatch.Web/.nvmrc) - nvm-windows
# is an nvm.exe and no function, and `nvm use` there is not wanted.
test-e2e: db
	bash -c 'export NVM_DIR="$$HOME/.nvm"; [ -s "$$NVM_DIR/nvm.sh" ] && \. "$$NVM_DIR/nvm.sh"; \
	set -e; \
	(cd ./src/Hatch.Web && if [ "$$(type -t nvm)" = function ]; then nvm use; fi && npm ci && npm run build -w apps/hatch); \
	HATCH_TEST_DATABASE_URL="Host=localhost;Port=5432;Database=hatch_test;Username=user;Password=password" \
		dotnet test ./src/Hatch.Api.Tests/Hatch.Api.Tests.csproj $(API_TEST_CLAMP) --filter "FullyQualifiedName~E2e"'

# One `npm ci` at the workspace root, then each app in turn. The apps are still
# named one at a time rather than run with `--workspaces` so that the "==>" line
# says which one is building when something fails.
test-web:
	bash -c 'export NVM_DIR="$$HOME/.nvm"; [ -s "$$NVM_DIR/nvm.sh" ] && \. "$$NVM_DIR/nvm.sh"; \
	set -e; \
	cd ./src/Hatch.Web && nvm use && npm ci; \
	for app in auth design hatch site; do \
		echo "==> $$app"; \
		npm run lint -w apps/$$app && npm run test --if-present -w apps/$$app && npm run build -w apps/$$app; \
	done'

# Which DbContext the ef-* targets act on. Defaults to the core schema; a module
# owns its own context and migrations folder (see src/Hatch.Api/Modules/README.md),
# so target one with e.g. `make ef-database-update context=StorageContext`.
context ?= AppDbContext

# make ef-migration migration=MyMigrationName
ef-migration:
	dotnet ef migrations add $(migration) --context $(context) --project ./src/Hatch.Api/Hatch.Api.csproj

# Applies pending EF migrations to the running `db` container without starting the full app (no HA/Quartz dependency).
ef-database-update:
	dotnet ef database update --context $(context) --project ./src/Hatch.Api/Hatch.Api.csproj

# Opens a psql shell against the running `db` container's hatch database, for manual inspection.
db-shell:
	docker compose exec db psql -U user -d hatch
