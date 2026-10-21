/* docsSearchField.ts (HA-1280): present on every docs page and the docs
   landing, and nowhere else - the sidebar's own server-rendered HTML carries
   no field and no listbox, with or without a sidebar at all. Regex and
   moduleIds over the built output: there is no DOM in this runner, so every
   search and keyboard decision stays docsSearch.test.ts's (HA-1063/HA-658) -
   this only checks what got built. */
/// <reference types="node" />
import { beforeAll, describe, expect, it } from 'vitest'
import { DOC_PAGES } from '../../hatch/src/lib/docs'
import { buildSite } from './testing/buildSite'
import { attr, tagsOf, withoutComments } from './testing/html'

type Site = Awaited<ReturnType<typeof buildSite>>

const DOCS_FILES = [...DOC_PAGES.map((p) => `docs/${p.slug}/index.html`), 'docs/index.html']
const NO_SIDEBAR_FILES = ['index.html', '404.html', 'setup/index.html']

const scriptSrcs = (html: string): (string | undefined)[] => tagsOf(withoutComments(html), 'script').map((tag) => attr(tag, 'src'))

const searchChunk = (site: Site) =>
  site.output.find((chunk) => chunk.type === 'chunk' && chunk.moduleIds.some((id) => id.endsWith('src/docsSearchField.ts')))!

let site: Site

beforeAll(async () => {
  site = await buildSite()
})

describe.each(DOCS_FILES)('%s', (file) => {
  it('carries the search script', () => {
    expect(scriptSrcs(site.read(file)).some((src) => src?.includes('docsSearchField'))).toBe(true)
  })

  it('carries no field and no listbox of its own - the script builds them, not renderSidebar', () => {
    expect(withoutComments(site.read(file))).not.toContain('site-docs-search')
  })
})

describe.each(NO_SIDEBAR_FILES)('%s', (file) => {
  it('carries neither the search script nor any .site-docs-search markup', () => {
    const html = withoutComments(site.read(file))
    expect(scriptSrcs(html).some((src) => src?.includes('docsSearchField'))).toBe(false)
    expect(html).not.toContain('site-docs-search')
  })
})

describe("the script's own chunk", () => {
  it('names the index path, holds the matcher\'s own code, and names no markdown', () => {
    const chunk = searchChunk(site)
    expect(chunk.moduleIds.some((id) => id.endsWith('src/lib/docs.ts') || id.endsWith('src/lib/docsIndex.ts') || id.includes('marked'))).toBe(
      false,
    )
    const text = site.read(chunk.fileName)
    expect(text).toContain('/docs/search-index.json')
    // searchDocs's own zero-match literal (lib/docsSearch.ts) - only present if
    // this chunk carries the real matcher rather than a reimplementation.
    expect(text).toContain('no page matches')
  })

  it('still names the index path under a base path, rewritten under it', async () => {
    const based = await buildSite({ base_path: 'project' })
    const text = based.read(searchChunk(based).fileName)
    expect(text).toContain('/project/docs/search-index.json')
  })
})
