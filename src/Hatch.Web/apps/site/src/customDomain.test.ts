/* One build shaped the way a custom domain shapes it: base_url set to the
   domain and base_path empty, which siteEnv turns into a base of /. Proves
   the origin and the root base work together - canonical/og:url, the
   sitemap and robots, the setup page's own address, and every asset and
   redirect still resolving under the root. The origin is invented here, in
   a test file the ethos scan skips, same as sitemap.test.ts. */
import { existsSync } from 'node:fs'
import { join } from 'node:path'
import { beforeAll, describe, expect, it } from 'vitest'
import { APP_BASENAME } from '../../hatch/src/lib/basename'
import { REDIRECTS } from '../vite-plugin-redirects'
import { buildSite } from './testing/buildSite'

type Site = Awaited<ReturnType<typeof buildSite>>
const ORIGIN = 'https://www.example.invalid'
// README.md: this app is static output on its own host, not served by Hatch.Api -
// so a docs link into the hatch app itself (left alone by resolveDocHref) names a
// file on a different host's build, never this one's. Same exclusion as links.test.ts.
const OTHER_APP = `${APP_BASENAME}/`

let site: Site
beforeAll(async () => {
  site = await buildSite({
    base_url: ORIGIN,
    base_path: '',
    GITHUB_SERVER_URL: 'https://git.example.invalid',
    GITHUB_REPOSITORY: 'owner/repo',
  })
})

const assetsOf = (html: string): string[] =>
  [...html.matchAll(/(?:src|href)="(\/[^"]*)"/g)]
    .map(([, path]) => path)
    .filter((path) => path !== '/')
    .filter((path) => !path.slice(1).startsWith('docs/'))
    .filter((path) => !path.startsWith(OTHER_APP))

const refreshTarget = (html: string): string => {
  const match = html.match(/content="0; url=([^"]*)"/)
  if (match === null) throw new Error('no refresh meta tag in stub')
  return match[1]
}

describe('a custom-domain build: base_url set, base_path empty', () => {
  it.each([
    ['index.html', ''],
    ['docs/index.html', 'docs/'],
    ['docs/getting-started/index.html', 'docs/getting-started/'],
    ['setup/index.html', 'setup/'],
  ])('%s carries exactly one canonical link and one og:url, at its own address', (file: string, path: string) => {
    const html = site.read(file)
    expect(html.match(/rel="canonical"/g)).toHaveLength(1)
    expect(html.match(/og:url/g)).toHaveLength(1)
    expect(html).toContain(`<link rel="canonical" href="${ORIGIN}/${path}" />`)
    expect(html).toContain(`<meta property="og:url" content="${ORIGIN}/${path}" />`)
  })

  it('404.html carries no canonical link and no og:url', () => {
    const html = site.read('404.html')
    expect(html).not.toMatch(/rel="canonical"/)
    expect(html).not.toMatch(/og:url/)
  })

  it('sitemap.xml lists every page at the origin, and no other origin', () => {
    const xml = site.read('sitemap.xml')
    const locs = [...xml.matchAll(/<loc>([\s\S]*?)<\/loc>/g)].map((match) => match[1])
    expect(locs.length).toBeGreaterThan(0)
    for (const loc of locs) {
      expect(loc.startsWith(`${ORIGIN}/`)).toBe(true)
      expect(loc.replace(`${ORIGIN}/`, '')).not.toMatch(/https:\/\//)
    }
  })

  it('robots.txt names the sitemap at the origin, and no other Sitemap line', () => {
    const robots = site.read('robots.txt')
    expect(robots).toContain(`Sitemap: ${ORIGIN}/sitemap.xml`)
    expect(robots.match(/^Sitemap:/gm)).toHaveLength(1)
  })

  it('setup/index.html gives its own address and not the fallback text', () => {
    const html = site.read('setup/index.html')
    expect(html).toContain(`Set me up with Hatch: ${ORIGIN}/setup/`)
    expect(html).not.toContain('the address of this page')
  })

  it.each(['index.html', '404.html', 'docs/index.html', 'docs/getting-started/index.html', 'setup/index.html'])(
    '%s only references assets that exist under dist/',
    (file: string) => {
      const html = site.read(file)
      for (const asset of assetsOf(html)) {
        expect(existsSync(join(site.dir, asset.slice(1)))).toBe(true)
      }
    },
  )

  it('each retired address refreshes to a root-relative docs address, with no base path segment', () => {
    for (const { from, to } of REDIRECTS) {
      const html = site.read(`${from}/index.html`)
      expect(refreshTarget(html)).toBe(`/docs/${to}/`)
    }
  })
})
