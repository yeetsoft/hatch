/* sitemap.xml: the home page, the docs landing, and one address per docs
   page, written only when the build has a canonical origin. Regex over the
   built text, the same as home.test.ts and docs.test.ts: there is no DOM in
   this runner. The origin is invented here, in a test file the ethos scan
   skips. */
/// <reference types="node" />
import { readdirSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import { renderSitemap } from '../vite-plugin-docs'
import { buildSite } from './testing/buildSite'

const DOCS_DIR = join(dirname(fileURLToPath(import.meta.url)), '../../hatch/docs')

const ORIGIN = 'https://site.example.invalid/project'

const locsOf = (xml: string): string[] => [...xml.matchAll(/<loc>([\s\S]*?)<\/loc>/g)].map((match) => match[1])

describe('the sitemap, under an environment', () => {
  it('lists the home page, the docs landing and one page per docs markdown file, with a base_url', async () => {
    const site = await buildSite({ base_url: ORIGIN })
    const xml = site.read('sitemap.xml')
    const mdCount = readdirSync(DOCS_DIR).filter((f) => f.endsWith('.md')).length

    const locs = locsOf(xml)
    expect(locs).toHaveLength(mdCount + 2)
    expect(new Set(locs).size).toBe(locs.length)
    expect(locs[0]).toBe(`${ORIGIN}/`)
    expect(locs[1]).toBe(`${ORIGIN}/docs/`)
    expect(locs.some((loc) => loc.endsWith('404.html'))).toBe(false)

    for (const loc of locs) {
      expect(loc.startsWith(`${ORIGIN}/`)).toBe(true)
      expect(loc.endsWith('/')).toBe(true)
      const path = loc.slice(`${ORIGIN}/`.length)
      expect(() => site.read(`${path}index.html`)).not.toThrow()
    }
  })

  it('makes no double slash in any <loc> when base_url carries a trailing slash', async () => {
    const site = await buildSite({ base_url: `${ORIGIN}/` })
    const xml = site.read('sitemap.xml')
    for (const loc of locsOf(xml)) expect(loc).not.toMatch(/([^:])\/\//)
  })

  it('writes no sitemap.xml with no base_url set', async () => {
    const site = await buildSite()
    expect(() => site.read('sitemap.xml')).toThrow()
    expect(site.read('index.html')).not.toContain('sitemap')
  })
})

describe('renderSitemap', () => {
  it('escapes an & in the origin', () => {
    const xml = renderSitemap('https://a&b.example.invalid/', [])
    expect(xml).toContain('https://a&amp;b.example.invalid/')
    expect(xml).not.toContain('a&b.example.invalid')
  })
})
