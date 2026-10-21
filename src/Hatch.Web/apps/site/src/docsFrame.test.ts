/* The frame around every docs page and the landing: the sidebar, in its desk and
   phone copies. Regex over the built text, the same as docsLanding.test.ts. */
/// <reference types="node" />
import { beforeAll, describe, expect, it } from 'vitest'
import { DOC_PAGES, docSections } from '../../hatch/src/lib/docs'
import { escapeHtml } from '../vite-plugin-site'
import { buildSite } from './testing/buildSite'
import { attr, tagsOf, withoutComments } from './testing/html'

type Site = Awaited<ReturnType<typeof buildSite>>

const sections = docSections(DOC_PAGES)
const hrefsInOrder = DOC_PAGES.map((p) => `/docs/${p.slug}/`)

/** The inside of every `<nav aria-label="LABEL">`, in order. Navs do not nest. */
export const blocks = (html: string, label: string): string[] =>
  [...html.matchAll(new RegExp(String.raw`<nav\b[^>]*aria-label="${label}"[^>]*>([\s\S]*?)</nav>`, 'g'))].map((m) => m[1])

/** The `href` of every link in `html`, in order. */
const linkHrefs = (html: string): string[] => tagsOf(html, 'a').map((tag) => attr(tag, 'href')!)

/** The text of every sidebar section heading in `html`. */
const headings = (html: string): string[] =>
  [...html.matchAll(/<p class="site-docs-sidebar-heading">([\s\S]*?)<\/p>/g)].map((m) => m[1])

/** The phone disclosure's opening tag and its summary text. */
const phoneDetails = (html: string): { tag: string; summary: string } => {
  const tag = tagsOf(html, 'details').find((t) => attr(t, 'class') === 'site-docs-sidebar-phone')!
  return { tag, summary: html.match(/<summary\b[^>]*>([\s\S]*?)<\/summary>/)![1] }
}

const pages = new Map<string, string>()
let landing: string

beforeAll(async () => {
  const site: Site = await buildSite()
  for (const page of DOC_PAGES) pages.set(page.slug, withoutComments(site.read(`docs/${page.slug}/index.html`)))
  landing = withoutComments(site.read('docs/index.html'))
})

describe.each(DOC_PAGES.map((p) => [p.slug, p] as const))('docs page %s', (slug, page) => {
  const html = () => pages.get(slug)!

  it('has exactly two Docs navs, each listing every page once, in order', () => {
    const navs = blocks(html(), 'Docs')
    expect(navs).toHaveLength(2)
    for (const nav of navs) expect(linkHrefs(nav)).toEqual(hrefsInOrder)
  })

  it("marks this page's own link, and only it, in each copy", () => {
    for (const nav of blocks(html(), 'Docs')) {
      const current = tagsOf(nav, 'a').filter((tag) => tag.includes('aria-current="page"'))
      expect(current).toHaveLength(1)
      expect(attr(current[0], 'href')).toBe(`/docs/${page.slug}/`)
    }
  })

  it('heads each copy with the sections in order', () => {
    for (const nav of blocks(html(), 'Docs')) {
      expect(headings(nav)).toEqual(sections.map((s) => escapeHtml(s.section)))
    }
  })

  it('has a closed phone disclosure summarised Pages', () => {
    const { tag, summary } = phoneDetails(html())
    expect(tag).not.toMatch(/\sopen\b/)
    expect(summary).toBe('Pages')
  })

  it('adds no script of its own', () => {
    if (html().includes('hatch-docs-mermaid')) return
    expect(tagsOf(html(), 'script')).toHaveLength(1)
  })
})

describe('docs landing', () => {
  it('has two Docs navs, each listing every page once, in order, none current', () => {
    const navs = blocks(landing, 'Docs')
    expect(navs).toHaveLength(2)
    for (const nav of navs) {
      expect(linkHrefs(nav)).toEqual(hrefsInOrder)
      expect(nav).not.toContain('aria-current')
    }
  })

  it('has a closed phone disclosure and no Changelog', () => {
    const { tag, summary } = phoneDetails(landing)
    expect(tag).not.toMatch(/\sopen\b/)
    expect(summary).toBe('Pages')
    expect(landing).not.toContain('Changelog')
  })
})

describe('under a base path', () => {
  it('links the sidebar under the base', async () => {
    const site = await buildSite({ base_path: 'project' })
    const html = withoutComments(site.read(`docs/${DOC_PAGES[0].slug}/index.html`))
    for (const nav of blocks(html, 'Docs')) {
      expect(linkHrefs(nav)).toEqual(DOC_PAGES.map((p) => `/project/docs/${p.slug}/`))
    }
  })
})
