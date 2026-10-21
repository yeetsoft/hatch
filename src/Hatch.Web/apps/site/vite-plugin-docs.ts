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

/** One heading in a page's outline, as `renderDocs` returns it. */
interface OutlineEntry {
  depth: 2 | 3
  text: string
  id: string
}

/** The shape `apps/hatch/src/lib/docs.ts` exports, read through `runnerImport` -
    not a static import, so the config bundle never sees that module's
    `import.meta.glob`. */
interface DocsModule {
  DOC_PAGES: DocsPage[]
  docSections(pages: DocsPage[]): LandingSection[]
  docNeighbours(pages: DocsPage[], slug: string): { previous: DocsPage | null; next: DocsPage | null }
  createSlugger(): (text: string) => string
  renderDocs(
    source: string,
    resolveHref?: (slug: string, fragment: string) => string,
  ): { html: string; outline: OutlineEntry[]; mermaidCount: number }
}

/** The two pages HA-663 has yet to write. A link to one of these still becomes
    a site address and does not fail the build - everything else does. */
export const PENDING_PAGES = new Set([
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
  /** Whether the page carries at least one mermaid fence - draws the one extra
      script that renders them. Omitted by `renderLanding`, which is never
      fenced. */
  mermaid?: boolean
}

/** The document every docs page shares - head, header, skip link, footer - so the
    footer's `<!--site:repository-->` block cannot drift between the landing and
    the pages. `title` and `description` are escaped here; `main` is trusted
    markup. */
function renderFrame({ title, description, main, up, mermaid }: Frame): string {
  const safeTitle = escapeHtml(title)
  const safeDescription = escapeHtml(description)
  const icon = `${up}packages/ui/src/assets/hatch-logo.png`
  const diagramsScript = mermaid ? '\n    <script type="module" src="/src/docsDiagrams.ts"></script>' : ''
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
    <script type="module" src="/src/theme.ts"></script>${diagramsScript}
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

/** The on-this-page outline: each `h2` and its `h3`s as links to the ids the app
    gives them, twice from one list - a phone `<details>` above the text and a
    desk `<nav>` beside it - for the reason the sidebar is. `''` when the page has
    no `h2` or `h3`, so it carries no empty outline. */
export function renderOutline(outline: readonly OutlineEntry[]): string {
  if (outline.length === 0) return ''
  const items = outline
    .map(
      (entry) =>
        `<li class="site-docs-outline-item site-docs-outline-item--d${entry.depth}"><a href="#${escapeHtml(entry.id)}">${escapeHtml(entry.text)}</a></li>`,
    )
    .join('')
  const list = `<ol class="site-docs-outline-list">${items}</ol>`
  return `      <details class="site-docs-outline-phone"><summary>On this page</summary><nav aria-label="On this page">${list}</nav></details>
      <nav class="site-docs-outline" aria-label="On this page"><p class="site-docs-outline-title">On this page</p>${list}</nav>`
}

/** The previous and next links at the foot of a docs page, each labelled with the
    page's title. `''` when both are null, so a one-page docs set carries no empty
    pager. A side that is null renders no link. */
export function renderPager(base: string, previous: DocsPage | null, next: DocsPage | null): string {
  if (previous === null && next === null) return ''
  const link = (page: DocsPage, direction: 'previous' | 'next'): string =>
    `<a class="site-docs-pager-link site-docs-pager-${direction}" rel="${direction === 'previous' ? 'prev' : 'next'}" href="${escapeHtml(docHref(base, page.slug, ''))}"><span class="site-docs-pager-label">${direction === 'previous' ? 'Previous' : 'Next'}</span><span class="site-docs-pager-title">${escapeHtml(page.title)}</span></a>`
  const links = [previous && link(previous, 'previous'), next && link(next, 'next')].filter(Boolean).join('')
  return `      <nav class="site-docs-pager" aria-label="Previous and next">${links}</nav>`
}

// The sitemap protocol's own namespace, stable since 2005 - structural, true
// of every installation, not this one's.
const SITEMAP_NAMESPACE = 'http://www.sitemaps.org/schemas/sitemap/0.9' // ethos:allow

/** `sitemap.xml`'s body: the home page, the docs landing, then one address per
    slug, each once, under `canonical` (already `siteEnv`-normalised, so it
    ends in `/`). Pure, so a test can call it with no build involved. */
export function renderSitemap(canonical: string, slugs: readonly string[]): string {
  const locs = [canonical, `${canonical}docs/`, ...slugs.map((slug) => `${canonical}docs/${slug}/`)]
  const urls = locs.map((loc) => `<url><loc>${escapeHtml(loc)}</loc></url>`).join('')
  return `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="${SITEMAP_NAMESPACE}">${urls}</urlset>\n`
}

function renderPage(
  page: DocsPage,
  rendered: { html: string; outline: readonly OutlineEntry[]; mermaidCount: number },
  base: string,
  sections: readonly LandingSection[],
  neighbours: { previous: DocsPage | null; next: DocsPage | null },
): string {
  const outline = renderOutline(rendered.outline)
  const outlined = outline === '' ? '' : ' site-docs--outlined'
  const pager = renderPager(base, neighbours.previous, neighbours.next)
  return renderFrame({
    title: `Hatch: ${page.title}`,
    description: page.summary,
    up: '../../../../',
    mermaid: rendered.mermaidCount > 0,
    main: `      <h1>${escapeHtml(page.title)}</h1>
      <div class="site-docs${outlined}">
${renderSidebar(base, sections, page.slug)}${outline === '' ? '' : `
${outline}`}
      <article class="hatch-docs-body">
${rendered.html}
      </article>${pager === '' ? '' : `
${pager}`}
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
 *
 * `canonical` (`siteEnv`'s own, `undefined` with no `base_url`) gates a
 * `sitemap.xml` written in `generateBundle`, once the build has a real
 * address to make every page's address absolute under.
 */
export function docsHtml(canonical: string | undefined): Plugin {
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
        const neighbours = module.docNeighbours(module.DOC_PAGES, page.slug)
        pages.set(id, renderPage(page, module.renderDocs(page.body, resolveHref), resolvedConfig.base, sections, neighbours))
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
    generateBundle() {
      if (canonical === undefined) return
      const module = docsModule!
      this.emitFile({
        type: 'asset',
        fileName: 'sitemap.xml',
        source: renderSitemap(canonical, module.DOC_PAGES.map((p) => p.slug)),
      })
    },
  }
}
