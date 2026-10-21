import type { Plugin } from 'vite'
import { runnerImport } from 'vite'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { escapeHtml } from './vite-plugin-site'

const __dirname = dirname(fileURLToPath(import.meta.url))

const LANDING_ID = resolve(__dirname, 'docs/index.html')

/** A page, as `apps/hatch/src/lib/docs.ts` exports it. */
interface DocsPage {
  slug: string
  title: string
  summary: string
  section: string
  body: string
}

/** One section of the landing, as `docSections` returns it. */
export interface LandingSection {
  section: string
  pages: { slug: string; title: string; summary: string }[]
}

/** The shape `apps/hatch/src/lib/docs.ts` exports, read through `runnerImport` -
    not a static import, so the config bundle never sees that module's
    `import.meta.glob`. */
interface DocsModule {
  DOC_PAGES: DocsPage[]
  docSections(pages: DocsPage[]): LandingSection[]
  createSlugger(): (text: string) => string
  renderDocs(source: string, resolveHref?: (slug: string, fragment: string) => string): { html: string }
}

/** The four pages HA-663 has yet to write. A link to one of these still becomes
    a site address and does not fail the build - everything else does. */
export const PENDING_PAGES = new Set([
  'playbooks',
  'questions-and-decisions',
  'review-pull-requests-and-the-merge-train',
  'flow-wip-and-the-battery',
])

/** Where a docs link to `slug` (with `fragment`, `#`-included or empty) goes on
    the site, under `base` (`config.base`: always `/` or `/<path>/`). */
export function docHref(base: string, slug: string, fragment: string): string {
  return `${base}docs/${slug}/${fragment}`
}

export interface BrokenDocsLink {
  page: string
  slug: string
}

/** Throws once, naming every link whose slug is neither a real page nor on
    `PENDING_PAGES` - not just the first. */
export function checkDocsLinks(links: readonly BrokenDocsLink[]): void {
  const broken = links.filter((l) => !PENDING_PAGES.has(l.slug))
  if (broken.length === 0) return
  throw new Error(broken.map((l) => `${l.page}.md links to "${l.slug}", which has no docs page`).join('\n'))
}

interface Frame {
  title: string
  description: string
  main: string
  /** The `../` prefix from the page's directory to `packages/ui/src/assets/`. */
  up: string
}

/** The document every docs page shares - head, header, skip link, footer - so the
    footer's `<!--site:repository-->` block cannot drift between the landing and
    the pages. `title` and `description` are escaped here; `main` is trusted
    markup. */
function renderFrame({ title, description, main, up }: Frame): string {
  const safeTitle = escapeHtml(title)
  const safeDescription = escapeHtml(description)
  const icon = `${up}packages/ui/src/assets/hatch-logo.png`
  return `<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>${safeTitle}</title>
    <meta name="description" content="${safeDescription}" />
    <meta property="og:title" content="${safeTitle}" />
    <meta property="og:description" content="${safeDescription}" />
    <meta property="og:type" content="website" />
    <link rel="icon" type="image/png" href="${icon}" />
    <link rel="stylesheet" href="/src/site.css" />
    <script type="module" src="/src/theme.ts"></script>
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

/** The docs sidebar: every section's pages as links, in order, twice from one
    list - a desk `<nav>` and a phone `<details>` - since a closed `<details>`
    cannot be forced open on the desk by CSS, and CSS shows one of the two. The
    link for `currentSlug` carries `aria-current="page"`; the landing passes none.
    The app's own sidebar leads Reference with its changelog; the public site has
    none, so there is no such entry here. */
export function renderSidebar(base: string, sections: readonly LandingSection[], currentSlug?: string): string {
  const groups = sections
    .map(({ section, pages }) => {
      const items = pages
        .map((page) => {
          const current = page.slug === currentSlug ? ' aria-current="page"' : ''
          return `<li><a href="${escapeHtml(docHref(base, page.slug, ''))}"${current}>${escapeHtml(page.title)}</a></li>`
        })
        .join('')
      return `<p class="site-docs-sidebar-heading">${escapeHtml(section)}</p><ul>${items}</ul>`
    })
    .join('')
  return `      <nav class="site-docs-sidebar" aria-label="Docs">${groups}</nav>
      <details class="site-docs-sidebar-phone"><summary>Pages</summary><nav aria-label="Docs">${groups}</nav></details>`
}

function renderPage(page: DocsPage, body: string, base: string, sections: readonly LandingSection[]): string {
  return renderFrame({
    title: `Hatch: ${page.title}`,
    description: page.summary,
    up: '../../../../',
    main: `      <h1>${escapeHtml(page.title)}</h1>
      <div class="site-docs">
${renderSidebar(base, sections, page.slug)}
      <article class="hatch-docs-body">
${body}
      </article>
      </div>`,
  })
}

const LANDING_DESCRIPTION =
  'Read the Hatch documentation: how to get a board running, the ideas it is built on, running agents, deploying it, and the full reference.'

/** The docs landing at `<base>docs/`: every section with at least one page, in
    order, each page's title linking to its own address beside its summary. The
    app's landing leads Reference with a link to its in-app changelog; the public
    site has no changelog, so there is no such entry here. Each section's id is
    `slugger(section)` - the rule the app gives its headings - so the home page's
    cards, which name those ids, are never tabled twice. */
export function renderLanding(base: string, sections: readonly LandingSection[], slugger: (text: string) => string): string {
  const blocks = sections.map(({ section, pages }) => {
    const id = escapeHtml(slugger(section))
    const items = pages
      .map(
        (page) => `          <li>
            <a href="${escapeHtml(docHref(base, page.slug, ''))}">${escapeHtml(page.title)}</a>
            <p class="hatch-docs-landing-summary">${escapeHtml(page.summary)}</p>
          </li>`,
      )
      .join('\n')
    return `        <section aria-labelledby="${id}">
          <h2 id="${id}">${escapeHtml(section)}</h2>
          <ul class="hatch-docs-landing-list">
${items}
          </ul>
        </section>`
  })
  return renderFrame({
    title: 'Hatch: Docs',
    description: LANDING_DESCRIPTION,
    up: '../../../',
    main: `      <h1>Docs</h1>
      <div class="site-docs">
${renderSidebar(base, sections)}
      <div class="hatch-docs-landing">
${blocks.join('\n')}
      </div>
      </div>`,
  })
}

/**
 * One virtual HTML input per page in `DOC_PAGES`, and one for the landing at
 * `docs/index.html`, read from the app's own docs
 * module through `runnerImport` so the parser and section order are never
 * duplicated here. Each id sits two directories deeper than `404.html`
 * (`docs/<slug>/index.html` vs `404.html`), which is why the frame's icon and
 * mark need two extra `../` to still reach `packages/ui/src/assets/`.
 *
 * The virtual inputs are registered in `config`, the hook that can still edit
 * `config.build` - but each page's HTML, which needs `base` to build its
 * hrefs, is only rendered in `configResolved`, once `base` is final.
 */
export function docsHtml(): Plugin {
  const pages = new Map<string, string>()
  let docsModule: DocsModule | undefined

  return {
    name: 'docs-html',
    async config(config) {
      const { module } = await runnerImport<DocsModule>(resolve(__dirname, '../hatch/src/lib/docs.ts'), {
        configFile: false,
        root: __dirname,
        logLevel: 'silent',
      })
      docsModule = module

      config.build ??= {}
      config.build.rolldownOptions ??= {}
      config.build.rolldownOptions.input ??= {}
      const input = config.build.rolldownOptions.input as Record<string, string>

      for (const page of module.DOC_PAGES) {
        input[`docs/${page.slug}`] = resolve(__dirname, `docs/${page.slug}/index.html`)
      }
      input['docs'] = LANDING_ID
    },
    configResolved(resolvedConfig) {
      const module = docsModule!
      const sections = module.docSections(module.DOC_PAGES)
      const knownSlugs = new Set(module.DOC_PAGES.map((p) => p.slug))
      const broken: BrokenDocsLink[] = []

      for (const page of module.DOC_PAGES) {
        const resolveHref = (slug: string, fragment: string): string => {
          if (!knownSlugs.has(slug)) broken.push({ page: page.slug, slug })
          return docHref(resolvedConfig.base, slug, fragment)
        }
        const id = resolve(__dirname, `docs/${page.slug}/index.html`)
        pages.set(id, renderPage(page, module.renderDocs(page.body, resolveHref).html, resolvedConfig.base, sections))
      }

      pages.set(LANDING_ID, renderLanding(resolvedConfig.base, sections, module.createSlugger()))

      checkDocsLinks(broken)
    },
    resolveId(id) {
      if (pages.has(id)) return id
    },
    load(id) {
      return pages.get(id)
    },
  }
}
