import { describe, expect, it } from 'vitest'
import { DOC_PAGES, renderDocs } from '../../hatch/src/lib/docs'
import { checkDocsLinks, docHref, PENDING_PAGES, renderLanding, type BrokenDocsLink } from '../vite-plugin-docs'

describe('docHref', () => {
  it('builds the four base/slug/fragment combinations', () => {
    expect(docHref('/', 'x', '')).toBe('/docs/x/')
    expect(docHref('/', 'x', '#y')).toBe('/docs/x/#y')
    expect(docHref('/project/', 'x', '')).toBe('/project/docs/x/')
    expect(docHref('/project/', 'x', '#y')).toBe('/project/docs/x/#y')
  })
})

describe('checkDocsLinks', () => {
  it('throws, naming the page and the slug, for an unknown link', () => {
    expect(() => checkDocsLinks([{ page: 'a', slug: 'bad' }])).toThrow('a')
    expect(() => checkDocsLinks([{ page: 'a', slug: 'bad' }])).toThrow('bad')
  })

  it('does not throw for a slug on PENDING_PAGES', () => {
    expect(() => checkDocsLinks([{ page: 'a', slug: 'playbooks' }])).not.toThrow()
  })

  it('lists every broken link, not just the first, and leaves out a pending one', () => {
    const links: BrokenDocsLink[] = [
      { page: 'a', slug: 'playbooks' },
      { page: 'b', slug: 'bad-one' },
      { page: 'c', slug: 'bad-two' },
    ]
    try {
      checkDocsLinks(links)
      expect.unreachable('expected checkDocsLinks to throw')
    } catch (error) {
      const message = (error as Error).message
      expect(message).toContain('bad-one')
      expect(message).toContain('bad-two')
      expect(message).not.toContain('playbooks')
    }
  })
})

describe('PENDING_PAGES', () => {
  it('equals the set of linked slugs with no page, computed from the content', () => {
    const knownSlugs = new Set(DOC_PAGES.map((p) => p.slug))
    const found = new Set<string>()
    for (const page of DOC_PAGES) {
      const html = renderDocs(page.body).html
      for (const match of html.matchAll(/\/apps\/hatch\/docs\/([a-z0-9-]+)/g)) {
        if (!knownSlugs.has(match[1])) found.add(match[1])
      }
    }
    expect([...found].sort()).toEqual([...PENDING_PAGES].sort())
  })
})

describe('renderLanding', () => {
  const sections = [
    { section: 'A & "B" <c>', pages: [{ slug: 'x', title: 'T <i> & "q"', summary: 'S <b> & "q"' }] },
  ]
  const html = renderLanding('/p/', sections, () => 'the-id')

  it('escapes the section, title and summary', () => {
    expect(html).toContain('A &amp; &quot;B&quot; &lt;c&gt;')
    expect(html).toContain('T &lt;i&gt; &amp; &quot;q&quot;')
    expect(html).toContain('S &lt;b&gt; &amp; &quot;q&quot;')
    expect(html).not.toContain('<i>')
    expect(html).not.toContain('<b>')
  })

  it("takes the section's id from the slugger and the link from the base", () => {
    expect(html).toContain('<h2 id="the-id">')
    expect(html).toContain('href="/p/docs/x/"')
  })
})
