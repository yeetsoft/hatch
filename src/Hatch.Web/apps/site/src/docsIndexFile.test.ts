/* docs/search-index.json: the flat search index, written at build time for
   HA-1280 to fetch - no page's HTML and no script carries it. */
/// <reference types="node" />
import { beforeAll, describe, expect, it } from 'vitest'
import { DOC_PAGES } from '../../hatch/src/lib/docs'
import { docIndex } from '../../hatch/src/lib/docsIndex'
import { buildSite } from './testing/buildSite'

type Site = Awaited<ReturnType<typeof buildSite>>

/** The ids of every `<h2>`-through-`<h4>` in `html`'s `<article>`, in order -
    `docsFrame.test.ts`'s own `headingIds` matches only `h2`/`h3`, which is
    `renderDocs`'s outline depth, not `docIndex`'s: an entry's anchor comes
    from any depth-2-through-4 heading, so this widens the class to match. */
const indexableHeadingIds = (html: string): string[] => {
  const article = html.match(/<article\b[^>]*>([\s\S]*?)<\/article>/)![1]
  return [...article.matchAll(/<h[234]\b[^>]*\sid="([^"]*)"/g)].map((m) => m[1])
}

let site: Site

beforeAll(async () => {
  site = await buildSite()
})

describe('docs/search-index.json', () => {
  it('deep-equals docIndex(DOC_PAGES) called directly', () => {
    const written = JSON.parse(site.read('docs/search-index.json'))
    expect(written).toEqual(docIndex(DOC_PAGES))
  })

  it("every entry's anchor is a real heading id on that entry's own page", () => {
    const entries = JSON.parse(site.read('docs/search-index.json')) as { slug: string; anchor: string | null }[]
    const idsOf = new Map<string, string[]>()
    for (const page of DOC_PAGES) {
      const html = site.read(`docs/${page.slug}/index.html`)
      idsOf.set(page.slug, indexableHeadingIds(html))
    }
    for (const entry of entries) {
      if (entry.anchor === null) continue
      expect(idsOf.get(entry.slug)).toContain(entry.anchor)
    }
  })

  it('still writes the same entries under a base path', async () => {
    const based = await buildSite({ base_path: 'project' })
    expect(JSON.parse(based.read('docs/search-index.json'))).toEqual(docIndex(DOC_PAGES))
  })

  it('is carried by no page HTML and no script', () => {
    const files = [
      ...DOC_PAGES.map((p) => `docs/${p.slug}/index.html`),
      'docs/index.html',
      'index.html',
      '404.html',
    ]
    const htmls = files.map((f) => site.read(f))
    const chunkTexts = site.output.filter((c) => c.type === 'chunk').map((c) => site.read(c.fileName))
    for (const text of [...htmls, ...chunkTexts]) {
      expect(text).not.toContain('"anchor":')
      expect(text).not.toContain('"summary":')
    }
  })
})
