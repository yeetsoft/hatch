/* The home page: the hero, Why Hatch, How it works, Docs. Regex over the
   built text: there is no DOM in this runner. */
/// <reference types="node" />
import { beforeAll, describe, expect, it } from 'vitest'
import { buildSite } from './testing/buildSite'
import { attr, attrValues, tagsOf, withoutComments } from './testing/html'

const HEADLINE = 'Hatch manages your AI coding team for you'

const DOCS_CARDS = [
  { name: 'Start here', id: 'start-here' },
  { name: 'Concepts', id: 'concepts' },
  { name: 'Running agents', id: 'running-agents' },
  { name: 'Deploying and operating', id: 'deploying-and-operating' },
  { name: 'Reference', id: 'reference' },
]

const homeOf = async (env?: Record<string, string | undefined>): Promise<string> =>
  withoutComments((await buildSite(env)).read('index.html'))

const sectionsOf = (html: string): string[] => html.match(/<section\b[\s\S]*?<\/section>/g) ?? []

const h2sOf = (html: string): string[] => [...html.matchAll(/<h2\b[^>]*>([\s\S]*?)<\/h2>/g)].map((match) => match[1].trim())

const sectionWithHeading = (html: string, heading: string): string =>
  sectionsOf(html).find((section) => h2sOf(section)[0] === heading) ?? ''

const liItemsOf = (html: string): string[] => html.match(/<li\b[^>]*>[\s\S]*?<\/li>/g) ?? []

describe('the home page', () => {
  let html: string
  beforeAll(async () => {
    html = await homeOf()
  })

  it('has exactly one h1, and it is the headline', () => {
    expect(html.match(/<h1\b/g)).toHaveLength(1)
    expect(html).toMatch(new RegExp(String.raw`<h1\b[^>]*>\s*${HEADLINE}\s*</h1>`))
  })

  it('says Hatch ships, offers to read the docs, and offers to run it', () => {
    expect(html).toContain('Hatch ships.')
    expect(html).toContain('Read the docs')
    expect(html).toContain('Run it')
  })

  it('installs with git clone, cd hatch, docker compose up -d, in that order', () => {
    const code = html.match(/<code>([\s\S]*?)<\/code>/)?.[1] ?? ''
    const lines = code.split(/\r?\n/).map((line) => line.trim())
    const positions = [/^git clone\b/, /^cd hatch$/, /^docker compose up -d$/].map((pattern) =>
      lines.findIndex((line) => pattern.test(line)),
    )
    expect(positions.every((at) => at >= 0)).toBe(true)
    expect([...positions].sort((a, b) => a - b)).toEqual(positions)
  })

  it('has the hero first, then Why Hatch, How it works, Docs, by <h2> position', () => {
    expect(html.indexOf('<h1')).toBeLessThan(html.indexOf('<h2'))
    expect(h2sOf(html)).toEqual(['Why Hatch', 'How it works', 'Docs'])
  })

  it('lists between four and six points under Why Hatch, each with a <strong>', () => {
    const section = sectionWithHeading(html, 'Why Hatch')
    const items = liItemsOf(section)
    expect(items.length).toBeGreaterThanOrEqual(4)
    expect(items.length).toBeLessThanOrEqual(6)
    for (const item of items) expect(item).toMatch(/<strong>/)
  })

  it('has exactly three paragraphs and two figures under How it works', () => {
    const section = sectionWithHeading(html, 'How it works')
    expect(section.match(/<p\b[^>]*>[\s\S]*?<\/p>/g) ?? []).toHaveLength(3)

    const figures = section.match(/<figure\b[^>]*>[\s\S]*?<\/figure>/g) ?? []
    expect(figures).toHaveLength(2)
    for (const figure of figures) {
      expect(attr(figure, 'class')).toBe('site-figure')
      expect(attr(figure, 'data-diagram')).toBeTruthy()
    }

    const captions = figures.map((figure) => figure.match(/<figcaption>([\s\S]*?)<\/figcaption>/)?.[1]?.trim())
    expect(captions).toEqual(['The moving parts', 'The life of a ticket'])

    const bodyOf = (figure: string) =>
      figure
        .replace(/^<figure\b[^>]*>/, '')
        .replace(/<figcaption>[\s\S]*?<\/figcaption>/, '')
        .replace(/<\/figure>$/, '')
        .trim()
    const figureNamed = (name: string) => figures.find((figure) => attr(figure, 'data-diagram') === name) ?? ''

    const movingParts = figureNamed('moving-parts')
    expect(movingParts.match(/<svg\b/g) ?? []).toHaveLength(2)
    expect(bodyOf(movingParts).replace(/<svg\b[\s\S]*?<\/svg>/g, '').trim()).toBe('')
    expect(movingParts.lastIndexOf('<figcaption>')).toBeGreaterThan(movingParts.lastIndexOf('</svg>'))

    const lifeOfATicket = figureNamed('life-of-a-ticket')
    expect(lifeOfATicket.match(/<svg\b/g) ?? []).toHaveLength(2)
    expect(bodyOf(lifeOfATicket).replace(/<svg\b[\s\S]*?<\/svg>/g, '').trim()).toBe('')
    expect(lifeOfATicket.lastIndexOf('<figcaption>')).toBeGreaterThan(lifeOfATicket.lastIndexOf('</svg>'))
  })

  it('lists the five Docs cards in order, each linking into the docs', () => {
    const section = sectionWithHeading(html, 'Docs')
    const items = liItemsOf(section)
    expect(items).toHaveLength(DOCS_CARDS.length)

    const names = items.map((item) => item.match(/<strong>([^<]+)<\/strong>/)?.[1])
    expect(names).toEqual(DOCS_CARDS.map((card) => card.name))

    const hrefs = attrValues(section, 'href')
    expect(hrefs).toHaveLength(DOCS_CARDS.length)
    DOCS_CARDS.forEach((card, i) => expect(hrefs[i]).toMatch(new RegExp(`/docs/#${card.id}$`)))
  })

  it('has a footer', () => {
    expect(tagsOf(html, 'footer').length).toBeGreaterThan(0)
  })
})

describe('the home page, under an environment', () => {
  it('points Read the docs under the base path', async () => {
    const html = await homeOf({ base_path: '/project' })
    expect(html).toMatch(/<a\b[^>]*href="\/project\/docs\/"[^>]*>Read the docs<\/a>/)
  })

  it('points Read the docs at /docs/ with no base path', async () => {
    const html = await homeOf()
    expect(html).toMatch(/<a\b[^>]*href="\/docs\/"[^>]*>Read the docs<\/a>/)
  })

  it('names the clone URL from the forge when it is set', async () => {
    const html = await homeOf({
      GITHUB_SERVER_URL: 'https://git.example.invalid',
      GITHUB_REPOSITORY: 'owner/repo',
    })
    expect(html).toContain('git clone https://git.example.invalid/owner/repo.git hatch')
  })
})
