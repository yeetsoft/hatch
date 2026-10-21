import { describe, expect, it } from 'vitest'
import { DOC_PAGES, renderDocs } from '../../hatch/src/lib/docs'
import { checkDocsLinks, docHref, PENDING_PAGES, renderLanding, renderOutline, renderPager, renderSidebar, type BrokenDocsLink } from '../vite-plugin-docs'

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
    expect(() => checkDocsLinks([{ page: 'a', slug: 'flow-wip-and-the-battery' }])).not.toThrow()
  })

  it('lists every broken link, not just the first, and leaves out a pending one', () => {
    const links: BrokenDocsLink[] = [
      { page: 'a', slug: 'flow-wip-and-the-battery' },
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
      expect(message).not.toContain('flow-wip-and-the-battery')
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

describe('renderSidebar', () => {
  const sections = [
    { section: 'A & "B" <c>', pages: [{ slug: 'x', title: 'T <i> & "q"', summary: '' }] },
    { section: 'Second', pages: [{ slug: 'y', title: 'Why', summary: '' }] },
  ]

  it('escapes the section and the title', () => {
    const html = renderSidebar('/', sections)
    expect(html).toContain('A &amp; &quot;B&quot; &lt;c&gt;')
    expect(html).toContain('T &lt;i&gt; &amp; &quot;q&quot;')
    expect(html).not.toContain('<i>')
    expect(html).not.toContain('<c>')
  })

  it('marks only the current page, once in each of the two copies', () => {
    const html = renderSidebar('/', sections, 'y')
    expect(html.match(/aria-current="page"/g)).toHaveLength(2)
    expect(html.match(/<a href="\/docs\/y\/" aria-current="page">Why<\/a>/g)).toHaveLength(2)
  })

  it('marks nothing without a current page, or for an unknown one', () => {
    expect(renderSidebar('/', sections)).not.toContain('aria-current')
    expect(renderSidebar('/', sections, 'nope')).not.toContain('aria-current')
  })

  it('has a desk nav and a closed phone disclosure, and no Changelog', () => {
    const html = renderSidebar('/', sections)
    expect(html).toContain('<nav class="site-docs-sidebar" aria-label="Docs">')
    expect(html).toContain('<details class="site-docs-sidebar-phone"><summary>Pages</summary>')
    expect(html).not.toMatch(/<details[^>]*\sopen\b/)
    expect(html).not.toContain('Changelog')
  })

  it('builds its links from the base', () => {
    expect(renderSidebar('/p/', sections)).toContain('href="/p/docs/x/"')
  })
})

describe('renderOutline', () => {
  it('is empty for no headings', () => {
    expect(renderOutline([])).toBe('')
  })

  it('escapes the text and the id', () => {
    const html = renderOutline([{ depth: 2, text: 'A < B & C', id: 'a"b' }])
    expect(html).toContain('A &lt; B &amp; C')
    expect(html).not.toContain('A < B')
    expect(html).toContain('href="#a&quot;b"')
  })

  it('marks each item by its depth', () => {
    const html = renderOutline([
      { depth: 2, text: 'One', id: 'one' },
      { depth: 3, text: 'Two', id: 'two' },
    ])
    expect(html).toContain('site-docs-outline-item--d2"><a href="#one">One')
    expect(html).toContain('site-docs-outline-item--d3"><a href="#two">Two')
  })

  it('links each heading to its id, in both copies', () => {
    const html = renderOutline([
      { depth: 2, text: 'One', id: 'one' },
      { depth: 3, text: 'Two', id: 'two' },
    ])
    expect([...html.matchAll(/href="([^"]*)"/g)].map((m) => m[1])).toEqual(['#one', '#two', '#one', '#two'])
    expect(html.match(/aria-label="On this page"/g)).toHaveLength(2)
  })
})

describe('renderPager', () => {
  const page = (slug: string, title: string) => ({ slug, title, summary: '', section: 'S', body: '' })
  const a = page('a', 'Page A')
  const b = page('b', 'Page B')

  it('is empty when both sides are null', () => {
    expect(renderPager('/', null, null)).toBe('')
  })

  it('links only the previous page when there is no next', () => {
    const html = renderPager('/', a, null)
    expect(html).toContain('<nav class="site-docs-pager" aria-label="Previous and next">')
    expect(html).toContain('rel="prev" href="/docs/a/"')
    expect(html).not.toContain('rel="next"')
    expect(html).not.toContain('>Next<')
  })

  it('links only the next page when there is no previous', () => {
    const html = renderPager('/', null, b)
    expect(html).toContain('rel="next" href="/docs/b/"')
    expect(html).not.toContain('rel="prev"')
    expect(html).not.toContain('>Previous<')
  })

  it('labels each link and puts the title beneath it', () => {
    const html = renderPager('/', a, b)
    expect(html).toContain('<span class="site-docs-pager-label">Previous</span><span class="site-docs-pager-title">Page A</span>')
    expect(html).toContain('<span class="site-docs-pager-label">Next</span><span class="site-docs-pager-title">Page B</span>')
    expect(html.indexOf('Page A')).toBeLessThan(html.indexOf('Page B'))
  })

  it('escapes the title', () => {
    const html = renderPager('/', page('x', 'A < B & "q"'), null)
    expect(html).toContain('A &lt; B &amp; &quot;q&quot;')
    expect(html).not.toContain('A < B')
  })

  it('builds its hrefs from the base', () => {
    const html = renderPager('/p/', a, b)
    expect(html).toContain('href="/p/docs/a/"')
    expect(html).toContain('href="/p/docs/b/"')
  })
})
