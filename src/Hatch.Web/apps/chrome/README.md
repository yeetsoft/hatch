# Hatch Chrome

The shared top bar, built for pages that are **not** Vite/React apps.

One page in the house is in that position: **Swagger UI**, Swashbuckle's own
document. Hatch reaches it only through the `<head>` content `UseSwaggerUI`
lets it inject (see `Program.cs`).

Without this build it had no bar, no way back but the browser's Back button,
and none of the Random palette the rest of the house wears. This build puts the
same bar on it, and dresses the page from the same tokens.

## What it produces

`npm run build -w apps/chrome` (the `BuildChrome` target in `Hatch.Api.csproj`
runs it) emits into `Hatch.Api/wwwroot/apps/chrome/`:

| File | What it is |
|---|---|
| `topbar.js` | The bar. Auto-mounts from `window.hatchTopBar`. |
| `topbar.css` | `@hatch/ui`'s tokens plus the bar's own three stylesheets. |
| `swagger.css` | Dresses Swagger UI from those tokens, so its dark mode and its palette are the house's. Copied verbatim from `public/`. |
| `assets/*.woff2` | The Manrope face, content-hashed by Vite. |

The names are fixed rather than content-hashed because `Program.cs` references
them by URL. See the note in `vite.config.ts` for why that is safe to serve.

## Using it

Set the global, then load the script:

```html
<link rel="stylesheet" href="/apps/chrome/topbar.css">
<script>window.hatchTopBar = { appName: 'Hatch API', theme: 'switch' };</script>
<script src="/apps/chrome/topbar.js"></script>
```

`theme` is `switch` (the page is themed — render the Auto/Light/Dark control),
or `light`/`dark` (the page has one appearance — pin it and render no control).
The options are documented in `packages/ui/src/standalone/topbar.ts`.

## Where the code is

Not here. `src/main.ts` is a single import; the bar is
`packages/ui/src/standalone/topbar.ts`, so it sits beside the React `<TopBar>`
whose stylesheet it shares. This workspace is only the build.
