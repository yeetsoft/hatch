/* docs/index.html, built by vite-plugin-docs.ts from the app's own docs module.
   Regex over the built text, the same as pages.test.ts: there is no DOM in this
   runner. */
/// <reference types="node" />
import { beforeAll, describe, expect, it } from 'vitest'
import { DOC_PAGES, createSlugger, docSections } from '../../hatch/src/lib/docs'
import { escapeHtml } from '../vite-plugin-site'
import { buildSite } from './testing/buildSite'
import { attr, tagsOf, withoutComments } from './testing/html'

type Site = Awaited<ReturnType<typeof buildSite>>

const sections = docSections(DOC_PAGES)

/** The text of every `<tag>…</tag>`, in order. */
const textsOf = (html: string, tag: string): string[] =>
  [...html.matchAll(new RegExp(String.raw`<${tag}\b[^>]*>([\s\S]*?)</${tag}>`, 'g'))].map((m) => m[1].trim())

/** The `<li>` link hrefs under `html`, in order. */
const linksIn = (html: string): string[] =>
  textsOf(html, 'li').map((li) => attr(li.match(/<a\b[^>]*>/)![0], 'href')!)

let html: string

beforeAll(async () => {
  const site: Site = await buildSite()
  html = withoutComments(site.read('docs/index.html'))
})

describe('docs landing', () => {
  it('has exactly one <h1>', () => {
    expect(tagsOf(html, 'h1')).toHaveLength(1)
  })

  it("has an <h2> for each section in DOC_SECTIONS order, with the app's heading id", () => {
    const slugger = createSlugger()
    const h2s = tagsOf(html, 'h2')
    expect(textsOf(html, 'h2')).toEqual(sections.map((s) => escapeHtml(s.section)))
    expect(h2s.map((tag) => attr(tag, 'id'))).toEqual(sections.map((s) => slugger(s.section)))
    expect(new Set(h2s.map((tag) => attr(tag, 'id'))).size).toBe(h2s.length)
  })

  it('lists every page once, in DOC_PAGES order, each with its summary', () => {
    expect(linksIn(html)).toEqual(sections.flatMap((s) => s.pages).map((p) => `/docs/${p.slug}/`))
    const items = textsOf(html, 'li')
    expect(items).toHaveLength(DOC_PAGES.length)
    sections
      .flatMap((s) => s.pages)
      .forEach((page, i) => {
        expect(items[i]).toContain(`>${escapeHtml(page.title)}</a>`)
        expect(items[i]).toContain(escapeHtml(page.summary))
      })
  })

  it("puts each page's link under its own section's <h2>", () => {
    const blocks = textsOf(html, 'section')
    expect(blocks).toHaveLength(sections.length)
    sections.forEach(({ pages }, i) => {
      expect(linksIn(blocks[i])).toEqual(pages.map((p) => `/docs/${p.slug}/`))
    })
  })

  it('has no Changelog entry', () => {
    expect(html).not.toContain('Changelog')
    expect(html).not.toContain('/changelog')
  })

  it('links the pages under a base path', async () => {
    const projectSite = await buildSite({ base_path: 'project' })
    const projectHtml = withoutComments(projectSite.read('docs/index.html'))
    expect(linksIn(projectHtml)).toEqual(sections.flatMap((s) => s.pages).map((p) => `/project/docs/${p.slug}/`))
  })
})
