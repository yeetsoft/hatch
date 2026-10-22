import { escapeHtml } from './vite-plugin-site'

export interface Frame {
  title: string
  description: string
  main: string
  /** The `../` prefix from the page's directory to `packages/ui/src/assets/`. */
  up: string
  /** Whether the page carries at least one mermaid fence - draws the one extra
      script that renders them. Omitted by `renderLanding`, which is never
      fenced. */
  mermaid?: boolean
  /** Whether the page carries a docs sidebar - inserts the script that builds
      the search field into it (HA-1280). Set by `renderPage` and
      `renderLanding`, both of which emit a sidebar; left unset by
      `contentHtml`'s pages, which emit none. */
  search?: boolean
  /** The page's own absolute address, carried as its canonical link and
      `og:url` when set (HA-1330); unset with no origin to build one under. */
  canonical?: string
}

/** The document every page shares - head, header, skip link, footer - so the
    footer's `<!--site:repository-->` block cannot drift between pages. `title`
    and `description` are escaped here; `main` is trusted markup. */
export function renderFrame({ title, description, main, up, mermaid, search, canonical }: Frame): string {
  const safeTitle = escapeHtml(title)
  const safeDescription = escapeHtml(description)
  const icon = `${up}packages/ui/src/assets/hatch-logo.png`
  const diagramsScript = mermaid ? '\n    <script type="module" src="/src/docsDiagrams.ts"></script>' : ''
  const searchScript = search ? '\n    <script type="module" src="/src/docsSearchField.ts"></script>' : ''
  const canonicalTags =
    canonical === undefined
      ? ''
      : `\n    <link rel="canonical" href="${escapeHtml(canonical)}" />\n    <meta property="og:url" content="${escapeHtml(canonical)}" />`
  return `<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>${safeTitle}</title>
    <meta name="description" content="${safeDescription}" />
    <meta property="og:title" content="${safeTitle}" />
    <meta property="og:description" content="${safeDescription}" />
    <meta property="og:type" content="website" />${canonicalTags}
    <link rel="icon" type="image/png" href="${icon}" />
    <link rel="stylesheet" href="/src/site.css" />
    <script type="module" src="/src/theme.ts"></script>${diagramsScript}${searchScript}
  </head>
  <body>
    <a class="site-skip" href="#main">Skip to content</a>
    <header class="site-header site-column">
      <a class="site-brand" href="%BASE_URL%">
        <img class="site-mark" src="${icon}" alt="" width="40" height="40" />
        <span>Hatch</span>
      </a>
      <nav class="site-nav" aria-label="Primary">
        <a href="%BASE_URL%docs/">Docs</a>
      </nav>
    </header>
    <main id="main" class="site-column">
${main}
    </main>
    <footer class="site-footer site-column">
      <!--site:repository-->
      <p>Hatch is open source.</p>
      <a href="{{repository-url}}">Source</a>
      <!--/site:repository-->
    </footer>
  </body>
</html>
`
}
