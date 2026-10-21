/* The home page: the hero, then Why Hatch, then the shells the next task
   fills. Regex over the built text: there is no DOM in this runner. */
/// <reference types="node" />
import { beforeAll, describe, expect, it } from 'vitest'
import { buildSite } from './testing/buildSite'

const withoutComments = (html: string): string => html.replace(/<!--[\s\S]*?-->/g, '')

const HEADLINE = 'Hatch manages your AI coding team for you'

const homeOf = async (env?: Record<string, string | undefined>): Promise<string> =>
  withoutComments((await buildSite(env)).read('index.html'))

const sectionsOf = (html: string): string[] => html.match(/<section\b[\s\S]*?<\/section>/g) ?? []

describe('the home page', () => {
  let html: string
  beforeAll(async () => {
    html = await homeOf()
  })

  it('has exactly one h1, and it is the headline', () => {
    expect(html.match(/<h1\b/g)).toHaveLength(1)
    expect(html).toMatch(new RegExp(String.raw`<h1\b[^>]*>\s*${HEADLINE}\s*</h1>`))
  })

  it('reads hero, actions, tagline, Why Hatch, How it works, Docs, in that order', () => {
    const positions = [
      'Read the docs',
      'class="site-run"',
      'Hatch ships.',
      '>Why Hatch<',
      '>How it works<',
      '>Docs<',
    ].map((needle) => html.indexOf(needle, html.indexOf('<main')))
    expect(positions.every((at) => at > 0)).toBe(true)
    expect([...positions].sort((a, b) => a - b)).toEqual(positions)
  })

  it('labels every section with a heading that exists', () => {
    const sections = sectionsOf(html)
    expect(sections.length).toBeGreaterThanOrEqual(4)
    for (const section of sections) {
      const id = section.match(/<section\b[^>]*\saria-labelledby="([^"]+)"/)?.[1]
      expect(id, 'a <section> without aria-labelledby').toBeTruthy()
      expect(html).toMatch(new RegExp(String.raw`\sid="${id}"`))
    }
  })

  it('lists four to six points, each a bold phrase and a sentence', () => {
    const list = html.match(/<ul class="site-points">([\s\S]*?)<\/ul>/)?.[1] ?? ''
    const points = list.match(/<li>\s*<strong>[^<]+<\/strong>[^<]+<\/li>/g) ?? []
    expect(points.length).toBeGreaterThanOrEqual(4)
    expect(points.length).toBeLessThanOrEqual(6)
    expect(list.match(/<li\b/g)).toHaveLength(points.length)
  })

  it('leaves How it works and Docs holding nothing but their heading', () => {
    for (const title of ['How it works', 'Docs']) {
      const section = sectionsOf(html).find((s) => s.includes(`>${title}<`))
      expect(section, title).toBeDefined()
      expect(section?.replace(/<\/?section\b[^>]*>/g, '').replace(/<h2\b[^>]*>[^<]*<\/h2>/, '').trim()).toBe('')
    }
  })

  it('names the repository nowhere but through the marker, with the three install lines', () => {
    const code = html.match(/<code>([\s\S]*?)<\/code>/)?.[1] ?? ''
    expect(code).toContain("git clone &lt;the repository's clone URL&gt; hatch")
    expect(code).toMatch(/^cd hatch\r?$/m)
    expect(code).toContain('docker compose up -d')
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
