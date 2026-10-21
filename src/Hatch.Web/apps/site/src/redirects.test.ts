/* The five redirect stubs: renderRedirect called directly, then a real build
   through buildSite() that reads each stub out of dist, pulls its refresh
   address out of it, and checks the page it names was actually written by
   that same build - so a stub can never point at a slug that has been
   renamed away. The origin is invented here, in a test file the ethos scan
   skips, same as sitemap.test.ts. */
import { describe, expect, it } from 'vitest'
import { renderRedirect, REDIRECTS } from '../vite-plugin-redirects'
import { buildSite } from './testing/buildSite'

const ORIGIN = 'https://site.example.invalid/project'

const refreshTarget = (html: string): string => {
  const match = html.match(/content="0; url=([^"]*)"/)
  if (match === null) throw new Error('no refresh meta tag in stub')
  return match[1]
}

describe('renderRedirect', () => {
  it('carries a canonical link at the given address when canonical is set', () => {
    const html = renderRedirect('/docs/the-runner/', 'https://a&b.example.invalid/docs/the-runner/')
    expect(html).toContain('<link rel="canonical" href="https://a&amp;b.example.invalid/docs/the-runner/" />')
  })

  it('carries no canonical link at all with no canonical', () => {
    const html = renderRedirect('/docs/the-runner/', undefined)
    expect(html).not.toContain('rel="canonical"')
  })

  it('refreshes to href and links it in the body', () => {
    const html = renderRedirect('/docs/the-runner/', undefined)
    expect(refreshTarget(html)).toBe('/docs/the-runner/')
    expect(html).toContain('<a href="/docs/the-runner/">')
  })
})

describe('the five retired addresses, under an environment', () => {
  it('each refresh to a relative-to-/ address with no canonical link, with no environment', async () => {
    const site = await buildSite()
    for (const { from, to } of REDIRECTS) {
      const html = site.read(`${from}/index.html`)
      const target = refreshTarget(html)
      expect(target.startsWith('/')).toBe(true)
      expect(html).not.toContain('rel="canonical"')
      expect(() => site.read(`docs/${to}/index.html`)).not.toThrow()
    }
  })

  it('refresh to an address under base_path, with base_path set', async () => {
    const site = await buildSite({ base_path: 'project' })
    for (const { from, to } of REDIRECTS) {
      const html = site.read(`${from}/index.html`)
      expect(refreshTarget(html)).toBe(`/project/docs/${to}/`)
    }
  })

  it('carry a canonical link at the absolute address, with base_url set', async () => {
    const site = await buildSite({ base_url: ORIGIN })
    for (const { from, to } of REDIRECTS) {
      const html = site.read(`${from}/index.html`)
      expect(html).toContain(`<link rel="canonical" href="${ORIGIN}/docs/${to}/" />`)
    }
  })
})
