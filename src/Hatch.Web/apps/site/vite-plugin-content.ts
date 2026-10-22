import type { Plugin } from 'vite'
import { runnerImport } from 'vite'
import { readFileSync, readdirSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { parseContentPage, type ContentPage } from './src/lib/content'
import { renderFrame } from './frame'
import { escapeHtml } from './vite-plugin-site'
import { checkDocsLinks, docHref, type BrokenDocsLink } from './vite-plugin-docs'

const __dirname = dirname(fileURLToPath(import.meta.url))
const CONTENT_DIR = resolve(__dirname, 'content')

/** The slice of `apps/hatch/src/lib/docs.ts`'s exports this plugin needs: the
    known slugs, for link validation, and the renderer itself. */
interface DocsModule {
  DOC_PAGES: { slug: string }[]
  renderDocs(
    source: string,
    resolveHref?: (slug: string, fragment: string) => string,
  ): { html: string; mermaidCount: number }
}

/**
 * One virtual HTML input per page under `apps/site/content/`, read from the
 * folder next to this file - no landing entry, unlike `docsHtml`. Each page
 * wears the site frame with no sidebar, outline or pager, and its one
 * `{{clone-url}}` marker becomes `cloneUrl`, or the fallback text, before the
 * markdown is rendered. A relative link to another docs page resolves against
 * `apps/hatch/src/lib/docs.ts`'s own pages, read once through `runnerImport`,
 * the same call `docsHtml()` already makes.
 */
export function contentHtml({ cloneUrl, canonical }: { cloneUrl: string | undefined; canonical: string | undefined }): Plugin {
  const pages = new Map<string, string>()
  let contentPages: ContentPage[] = []

  return {
    name: 'content-html',
    config(config) {
      contentPages = readdirSync(CONTENT_DIR)
        .filter((f) => f.endsWith('.md'))
        .map((f) => parseContentPage(`content/${f}`, readFileSync(resolve(CONTENT_DIR, f), 'utf8')))

      config.build ??= {}
      config.build.rolldownOptions ??= {}
      config.build.rolldownOptions.input ??= {}
      const input = config.build.rolldownOptions.input as Record<string, string>

      for (const page of contentPages) {
        input[page.slug] = resolve(__dirname, `${page.slug}/index.html`)
      }
    },
    async configResolved(resolvedConfig) {
      const { module } = await runnerImport<DocsModule>(resolve(__dirname, '../hatch/src/lib/docs.ts'), {
        configFile: false,
        root: __dirname,
        logLevel: 'silent',
      })
      const knownSlugs = new Set(module.DOC_PAGES.map((p) => p.slug))
      const broken: BrokenDocsLink[] = []

      for (const page of contentPages) {
        const resolveHref = (slug: string, fragment: string): string => {
          if (slug === 'index') return resolvedConfig.base
          if (!knownSlugs.has(slug)) broken.push({ page: page.slug, slug })
          return docHref(resolvedConfig.base, slug, fragment)
        }
        const pageAddress = canonical === undefined ? undefined : `${canonical}${page.slug}/`
        const body = page.body
          .replaceAll('{{clone-url}}', cloneUrl ?? "<the repository's clone URL>")
          .replaceAll('<the address of this page>', pageAddress ?? '<the address of this page>')
        const rendered = module.renderDocs(body, resolveHref)
        const id = resolve(__dirname, `${page.slug}/index.html`)
        pages.set(
          id,
          renderFrame({
            title: `Hatch: ${page.title}`,
            description: page.summary,
            up: '../../../',
            mermaid: rendered.mermaidCount > 0,
            canonical: pageAddress,
            main: `      <h1>${escapeHtml(page.title)}</h1>
      <article class="hatch-docs-body">
${rendered.html}
      </article>`,
          }),
        )
      }

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
