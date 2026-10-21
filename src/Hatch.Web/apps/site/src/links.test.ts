/* The home page, 404 page and a docs page: every link they make good on.
   Regex over the built text, the same as pages.test.ts: there is no DOM in
   this runner. */
/// <reference types="node" />
import { existsSync } from 'node:fs'
import { join } from 'node:path'
import { beforeAll, describe, expect, it } from 'vitest'
import { APP_BASENAME } from '../../hatch/src/lib/basename'
import { PENDING_PAGES } from '../vite-plugin-docs'
import { buildSite } from './testing/buildSite'
import { attr, attrValues, hrefs, ids, tagsOf, withoutComments } from './testing/html'

type Site = Awaited<ReturnType<typeof buildSite>>

// README.md: this app is static output on its own host, not served by Hatch.Api -
// so a docs link into the hatch app itself (left alone by resolveDocHref) names a
// file on a different host's build, never this one's.
const OTHER_APP = `${APP_BASENAME}/`

let site: Site

beforeAll(async () => {
  site = await buildSite()
})

/** A root-relative ref that names a file this build writes: not another app's,
    and not a page HA-663 has yet to write. */
function isOwnFile(ref: string): boolean {
  if (ref.startsWith(OTHER_APP)) return false
  const slug = ref.match(/^\/docs\/([a-z0-9-]+)\//)?.[1]
  return slug === undefined || !PENDING_PAGES.has(slug)
}

const fileOf = (path: string): string => (path.endsWith('/') ? `${path.slice(1)}index.html` : path.slice(1))

describe.each(['index.html', '404.html', 'docs/index.html', 'docs/getting-started/index.html'])('%s', (file: string) => {
  let html: string
  beforeAll(() => {
    html = withoutComments(site.read(file))
  })

  it('links home', () => {
    expect(hrefs(html)).toContain('/')
  })

  it('has no target="_blank"', () => {
    for (const tag of tagsOf(html, 'a')) expect(attr(tag, 'target')).not.toBe('_blank')
  })

  it('every href="#x" names an id in the same page', () => {
    const fragments = hrefs(html)
      .filter((href) => href.startsWith('#'))
      .map((href) => href.slice(1))
    expect(fragments.length).toBeGreaterThan(0)
    const pageIds = ids(html)
    for (const fragment of fragments) expect(pageIds).toContain(fragment)
  })

  it('every asset or page of this site names a file in the output directory', () => {
    const refs = [...hrefs(html), ...attrValues(html, 'src')]
      .filter((ref) => ref.startsWith('/'))
      .map((ref) => ref.split('#')[0]) // drop a #fragment before checking the file exists
      .filter(isOwnFile)
    expect(refs.length).toBeGreaterThan(0)
    for (const ref of refs) {
      expect(existsSync(join(site.dir, fileOf(ref))), ref).toBe(true)
    }
  })
})

// A /path/#id link into another page of this site lands on an id that page has.
// This covers the home page's Docs cards with no table of their own, and the docs'
// own cross-page anchors.
describe('cross-page anchors', () => {
  const FROM = ['index.html', '404.html', 'docs/index.html', 'docs/getting-started/index.html']
  const CARDS = ['start-here', 'concepts', 'running-agents', 'deploying-and-operating', 'reference'].map(
    (id) => `/docs/#${id}`,
  )

  const checked = (file: string): string[] =>
    hrefs(withoutComments(site.read(file)))
      .filter((href) => /^\/[^#]*#./.test(href))
      .filter((href) => isOwnFile(href.split('#')[0]))

  it.each(FROM)('every /…#id link in %s lands on an id in the page it names', (file: string) => {
    for (const href of checked(file)) {
      const [path, fragment] = href.split('#')
      expect(ids(withoutComments(site.read(fileOf(path)))), `${file}: ${href}`).toContain(fragment)
    }
  })

  it('checks something', () => {
    expect(FROM.flatMap(checked).length).toBeGreaterThan(0)
  })

  it("checks the home page's five Docs cards", () => {
    expect(checked('index.html')).toEqual(expect.arrayContaining(CARDS))
  })
})
