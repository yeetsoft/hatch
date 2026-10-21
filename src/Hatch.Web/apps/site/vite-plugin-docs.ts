import type { Plugin } from 'vite'
import { runnerImport } from 'vite'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { escapeHtml } from './vite-plugin-site'

const __dirname = dirname(fileURLToPath(import.meta.url))

/** The shape `apps/hatch/src/lib/docs.ts` exports, read through `runnerImport` -
    not a static import, so the config bundle never sees that module's
    `import.meta.glob`. */
interface DocsModule {
  DOC_PAGES: { slug: string; title: string; summary: string; body: string }[]
  renderDocs(source: string): { html: string }
}

function renderPage(page: DocsModule['DOC_PAGES'][number], body: string): string {
  const title = escapeHtml(`Hatch: ${page.title}`)
  const description = escapeHtml(page.summary)
  return `<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>${title}</title>
    <meta name="description" content="${description}" />
    <meta property="og:title" content="${title}" />
    <meta property="og:description" content="${description}" />
    <meta property="og:type" content="website" />
    <link rel="icon" type="image/png" href="../../../../packages/ui/src/assets/hatch-logo.png" />
    <link rel="stylesheet" href="/src/site.css" />
    <script type="module" src="/src/theme.ts"></script>
  </head>
  <body>
    <a class="site-skip" href="#main">Skip to content</a>
    <header class="site-header site-column">
      <a class="site-brand" href="%BASE_URL%">
        <img class="site-mark" src="../../../../packages/ui/src/assets/hatch-logo.png" alt="" width="40" height="40" />
        <span>Hatch</span>
      </a>
      <nav class="site-nav" aria-label="Primary">
        <a href="%BASE_URL%docs/">Docs</a>
      </nav>
    </header>
    <main id="main" class="site-column">
      <h1>${escapeHtml(page.title)}</h1>
      <article class="hatch-docs-body">
${body}
      </article>
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

/**
 * One virtual HTML input per page in `DOC_PAGES`, read from the app's own docs
 * module through `runnerImport` so the parser and section order are never
 * duplicated here. Each id sits two directories deeper than `404.html`
 * (`docs/<slug>/index.html` vs `404.html`), which is why the frame's icon and
 * mark need two extra `../` to still reach `packages/ui/src/assets/`.
 */
export function docsHtml(): Plugin {
  const pages = new Map<string, string>()

  return {
    name: 'docs-html',
    async config(config) {
      const { module } = await runnerImport<DocsModule>(resolve(__dirname, '../hatch/src/lib/docs.ts'), {
        configFile: false,
        root: __dirname,
        logLevel: 'silent',
      })

      config.build ??= {}
      config.build.rolldownOptions ??= {}
      config.build.rolldownOptions.input ??= {}
      const input = config.build.rolldownOptions.input as Record<string, string>

      for (const page of module.DOC_PAGES) {
        const id = resolve(__dirname, `docs/${page.slug}/index.html`)
        pages.set(id, renderPage(page, module.renderDocs(page.body).html))
        input[`docs/${page.slug}`] = id
      }
    },
    resolveId(id) {
      if (pages.has(id)) return id
    },
    load(id) {
      return pages.get(id)
    },
  }
}
