# Hatch Site

The marketing page: what Hatch is, for someone who has not cloned it yet.

## Development

### Prerequisites

- Node.js >= 22

### Setup

Hatch.Web is a single npm workspace with one lockfile at `src/Hatch.Web`, so the
install is the workspace's rather than this app's — run it once, from anywhere
in the tree, and every app is installed:

```bash
npm install
```

### Run

```bash
npm run dev -w apps/site
```

### Preview a build

```bash
npm run build -w apps/site
npm run preview -w apps/site
```

The build writes to `apps/site/dist/`. `vite preview` serves `index.html` and
answers an unknown path with 404, the way the real deploy does — but it does
not serve `404.html` itself; open `/404.html` directly to see that page.

The docs landing at `/docs/` and the pages under `/docs/<slug>/` are virtual build inputs
(`vite-plugin-docs.ts`), so `npm run dev` does not serve them — only `build`
and `preview` do. Each docs page carries a sidebar, an outline and a
previous/next pager, all built in `vite-plugin-docs.ts` with no script.

### Environment variables

Four variables, none of which a dev checkout sets, shape the build:

- `base_path` — the site's base path, e.g. `/hatch`. Everything the build
  emits is rooted under it.
- `base_url` — the canonical origin. Adds a `<link rel="canonical">` and an
  `og:url` meta tag pointing at it.
- `GITHUB_SERVER_URL` and `GITHUB_REPOSITORY` — together, the link to the
  source repository in the footer and the clone URL on the home page.

With none of them set, the build writes no canonical URL, no repository
link, and a base path of `/`.

## Not plumbed into Hatch.Api

Unlike `apps/design` and `apps/hatch`, this app is not served by `Hatch.Api` —
it is static output a separate host (GitHub Pages) serves directly. Of the
five-item plumbing tax in
[`docs/design-system-architecture.md`](../../../../docs/design-system-architecture.md),
it takes items 4 and 5 (the CI matrix, the `Makefile`'s `test-web` loop) and
deliberately not items 1–3: no `Hatch.Api.csproj` build target, no
`Dockerfile.api` `COPY`, no `Program.cs` redirect or fallback route.

The API image's `web-build` stage still ends in `build --workspaces
--if-present`, so the image builds this app too, into an `apps/site/dist/` that
nothing copies out; the build is hermetic, so that is harmless, and the app is
not added to the image's `COPY` list.
