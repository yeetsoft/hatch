/* Proves the four variables siteEnv() reads actually reach a build: unset,
   neither the canonical link nor the repository link appears anywhere, and
   every asset a page references exists on disk; set, both appear with the
   exact composed values and every asset sits under the base path. Regex over
   the built text, the same as pages.test.ts: there is no DOM in this runner. */
/// <reference types="node" />
import { existsSync } from 'node:fs'
import { join } from 'node:path'
import { beforeAll, describe, expect, it } from 'vitest'
import { buildSite } from './testing/buildSite'

type Site = Awaited<ReturnType<typeof buildSite>>

/* Every root-relative src=/href= under the given base, except the Docs card's
   links - those resolve once HA-724 lands and are not this test's to check. */
const assetsOf = (html: string, base: string): string[] =>
  [...html.matchAll(/(?:src|href)="(\/[^"]*)"/g)]
    .map(([, path]) => path)
    .filter((path) => path !== base)
    .filter((path) => !path.slice(base.length).startsWith('docs/'))

describe('with nothing set', () => {
  let site: Site
  beforeAll(async () => {
    site = await buildSite()
  })

  it.each(['index.html', '404.html'])('%s has no canonical link and no og:url', (file: string) => {
    const html = site.read(file)
    expect(html).not.toMatch(/rel="canonical"/)
    expect(html).not.toMatch(/og:url/)
  })

  it.each(['index.html', '404.html'])('%s has no repository link and no empty <a>', (file: string) => {
    const html = site.read(file)
    expect(html).not.toMatch(/site:repository/)
    expect(html).not.toMatch(/<a[^>]*>\s*<\/a>/)
    expect(html).not.toMatch(/<a href="">/)
  })

  it.each(['index.html', '404.html'])('%s only references assets that exist under dist/', (file: string) => {
    const html = site.read(file)
    const assets = assetsOf(html, '/')
    expect(assets.length).toBeGreaterThan(0)
    for (const asset of assets) {
      expect(existsSync(join(site.dir, asset.replace(/^\//, '')))).toBe(true)
    }
  })
})

describe('with base_url, base_path, GITHUB_SERVER_URL and GITHUB_REPOSITORY set', () => {
  let site: Site
  const base_url = 'https://example.invalid/'
  const base_path = '/project'
  const GITHUB_SERVER_URL = 'https://git.example.invalid'
  const GITHUB_REPOSITORY = 'owner/repo'
  const canonical = 'https://example.invalid/'
  const repositoryUrl = 'https://git.example.invalid/owner/repo'

  beforeAll(async () => {
    site = await buildSite({ base_url, base_path, GITHUB_SERVER_URL, GITHUB_REPOSITORY })
  })

  it('index.html carries the composed canonical link and og:url', () => {
    const html = site.read('index.html')
    expect(html).toContain(`<link rel="canonical" href="${canonical}" />`)
    expect(html).toContain(`<meta property="og:url" content="${canonical}" />`)
  })

  it('404.html carries no canonical link, since its <head> carries no marker', () => {
    const html = site.read('404.html')
    expect(html).not.toMatch(/rel="canonical"/)
    expect(html).not.toMatch(/og:url/)
  })

  it('index.html carries the composed repository link', () => {
    const html = site.read('index.html')
    expect(html).toContain(`<a href="${repositoryUrl}">Source</a>`)
  })

  it('404.html carries the composed repository link', () => {
    const html = site.read('404.html')
    expect(html).toContain(`<a href="${repositoryUrl}">Source</a>`)
  })

  it.each(['index.html', '404.html'])('%s only references assets under the base path', (file: string) => {
    const html = site.read(file)
    const assets = assetsOf(html, `${base_path}/`)
    expect(assets.length).toBeGreaterThan(0)
    for (const asset of assets) {
      expect(asset.startsWith(`${base_path}/`)).toBe(true)
    }
  })
})
