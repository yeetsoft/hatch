# Hatch: seeing a screen render

Read this when you change a web app and need to see the screen, not stop at
"it builds". The driver is [Playwright](https://playwright.dev/dotnet/) through
its .NET binding, in `src/Hatch.Api.Tests`: it runs a real browser headlessly
against the real API serving the built bundle, so a case can assert on a page
and a session with no display can still look at one. Why it is this and not the
Vite dev server is HA-1030's to say, not this file's.

## Install the browsers

```
make browsers
```

Once per machine, and again whenever the `Microsoft.Playwright` package version
moves. It builds the test project, then installs Chromium and WebKit into
Playwright's own per-user cache, not this checkout. It needs PowerShell 7
(`pwsh`) on `PATH` — the installer is a PowerShell script.

## Run the browser cases

```
make db
make test-e2e
```

`make db` first: the cases make their own scratch databases beside the test
database. `make test-e2e` builds the hatch bundle and runs only the browser
cases.

**WebKit on macOS 14 does not open a page.** Playwright stopped updating its
WebKit build for macOS 14 (`make browsers` prints "frozen webkit browser"), and
the current client sets `PushAPIEnabled` on every new page, which that build
rejects as an unknown setting; the close that follows (`Playwright.closePage`)
is missing from it too, so the page never opens and the call never returns. The
Chromium smoke passes there; the two `iPhone 14 Pro` cases time out inside
`opening the page`, leaving a `trace.zip` with no `page.png`. Playwright's
warning says to update the OS; the builds for newer macOS releases are the ones
it still updates.

## Where a case lives

`src/Hatch.Api.Tests/E2e/`, built on `E2eHarness.cs`, which starts the API on
a port the OS picks, with its own scratch databases, and hands a case a page.
The board smoke, `BoardRendersTests.cs`, is the one to copy: it asserts the
top bar (`E2eHarness.TopBar`, `header.hatch-topbar`), a column
(`E2eHarness.Column`, `.hatch-column`), and that the page threw no uncaught
error (`AssertNoPageErrors`).

The smoke runs in Chromium, headless. The `iPhone 14 Pro` profile runs in
WebKit.

## Render any page with no code written

The screenshot CLI renders one URL to one image. It is `playwright.ps1`, built
beside the test binary by `make browsers`, and run under `pwsh` on every
platform.

From a fresh checkout, in order:

1. **Build the bundle.** `npm run build -w apps/hatch` in `src/Hatch.Web`
   (`make test-e2e` does it too). `Program.cs` maps `/apps/hatch` only when
   `src/Hatch.Api/wwwroot/apps/hatch` exists; without it the page is a 404.
2. **Migrate.** `HATCH_MIGRATE=1 make run` migrates and exits. Compose's
   `migrate` service is the same thing. A serving process never migrates on
   its own.
3. **Serve.** `make run`, and read the address off its own `Now listening on:`
   line. Under compose it is `docker compose ps api`, and `HATCH_PORT`. Use the
   address your server printed; this file writes none.
4. **Render.**

   ```
   pwsh src/Hatch.Api.Tests/bin/Debug/net10.0/playwright.ps1 screenshot <address>/apps/hatch/ <path>.png --device="iPhone 14 Pro" --wait-for-selector=".hatch-column"
   ```

`<address>` is the one step 3 printed. The page paths are `/apps/hatch/` for the
board, `/apps/hatch/issues/<key>` for an issue and `/apps/hatch/plan` for the
plan.

- **`--device="iPhone 14 Pro"` alone picks WebKit** — the descriptor carries its
  browser — at a 393×660 CSS-px viewport (the descriptor's viewport, not its
  393×852 screen) and 3× scale, so the image is 1179×1980.
- **Always name something to wait for.** The CLI captures at load, before the app
  has drawn: without `--wait-for-selector` a page that renders client-side comes
  back as an empty background. `.hatch-column` is the board's; pick the one
  element the page you are looking at cannot be without.
- **`--color-scheme dark` renders night.** A fresh profile has no stored
  `hatch.theme`, so `auto` defers to `prefers-color-scheme`. Light is the default.
- **No sign-in locally.** The wall is off in Development (`Auth:Enabled` in
  `appsettings.Development.json`) and in compose (`HATCH_AUTH`, off by default),
  so a local `make run` or compose stack needs none. Against a Hatch with the
  wall up, `--load-storage` is the way in.
- **Never the Vite dev server.** What the browser renders has to be what ships.
- `screenshot --help` also lists `--viewport-size`, `--full-page` and the rest.

## Open a trace

```
pwsh src/Hatch.Api.Tests/bin/Debug/net10.0/playwright.ps1 show-trace <trace.zip>
```

This opens a window, so it is a person's tool; an agent reads `page.png`
instead. Never send anyone to an online trace viewer: opening a trace there
uploads it.

A failed case leaves both under `src/Hatch.Api.Tests/TestResults/e2e/<case>-<pid>/`
(`E2eHarness.LeaveEvidenceAsync`). A passing case leaves nothing.

## Rendering two checkouts side by side

Do not, with the CLI. `make run` holds one fixed port (`launchSettings.json`) and
the compose stack is one per machine (`name: hatch-local`), so a second
checkout's screenshot can show whichever checkout's build holds the port, not
its own. Side by side, the safe rendering is an e2e case: the harness binds a
loopback port Kestrel picks (port `0`) and makes a scratch database pair per process
(`MinimalDatabases`), so two cannot meet.

A session that could not render for this reason says so on its ticket, and why.

## What to write on the ticket

In place of "it builds":

- the page's **path**, never a URL with a host in it;
- the **profile** — `iPhone 14 Pro`, or the viewport size and colour scheme;
- **what was seen**.

The image goes under the gitignored `src/Hatch.Api.Tests/TestResults/` or the OS
temp directory, never a tracked path: tickets take no attachments, so the words
are what the next reader gets.
