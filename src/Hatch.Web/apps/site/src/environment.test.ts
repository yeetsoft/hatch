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

/* Every root-relative src=/href= under the given base, except the links
   into docs/ - a /docs/#x ref names a page and a fragment, not a file, and
   links.test.ts checks those. */
const assetsOf = (html: string, base: string): string[] =>
  [...html.matchAll(/(?:src|href)="(\/[^"]*)"/g)]
    .map(([, path]) => path)
    .filter((path) => path !== base)
    .filter((path) => !path.slice(base.length).startsWith('docs/'))

/* Every src=/href= value in the page, relative references included - unlike
   assetsOf, which only sees root-relative ones. Used to prove 404.html, served
   at any depth under GitHub Pages, carries no relative reference that would
   break below the first level. */
const allRefsOf = (html: string): string[] => [...html.matchAll(/(?:src|href)="([^"]*)"/g)].map(([, path]) => path)

describe('with nothing set', () => {
  let site: Site
  beforeAll(async () => {
    site = await buildSite()
  })

  it.each(['index.html', '404.html', 'docs/index.html', 'docs/getting-started/index.html', 'setup/index.html'])(
    '%s has no canonical link and no og:url',
    (file: string) => {
      const html = site.read(file)
      expect(html).not.toMatch(/rel="canonical"/)
      expect(html).not.toMatch(/og:url/)
    },
  )

  it('404.html carries no relative src or href - every one is root-relative or a fragment', () => {
    const html = site.read('404.html')
    for (const ref of allRefsOf(html)) {
      expect(ref).toMatch(/^\/|^#/)
    }
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

  it.each([
    ['index.html', canonical],
    ['docs/index.html', `${canonical}docs/`],
    ['docs/getting-started/index.html', `${canonical}docs/getting-started/`],
    ['setup/index.html', `${canonical}setup/`],
  ])('%s carries exactly one canonical link and one og:url, at its own address', (file: string, address: string) => {
    const html = site.read(file)
    expect(html.match(/rel="canonical"/g)).toHaveLength(1)
    expect(html.match(/og:url/g)).toHaveLength(1)
    expect(html).toContain(`<link rel="canonical" href="${address}" />`)
    expect(html).toContain(`<meta property="og:url" content="${address}" />`)
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
