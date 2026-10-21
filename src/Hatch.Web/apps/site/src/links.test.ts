/* The home page and 404 page: every link they make good on. Regex over the
   built text, the same as pages.test.ts: there is no DOM in this runner. */
/// <reference types="node" />
import { existsSync } from 'node:fs'
import { join } from 'node:path'
import { beforeAll, describe, expect, it } from 'vitest'
import { buildSite } from './testing/buildSite'
import { attr, attrValues, hrefs, ids, tagsOf, withoutComments } from './testing/html'

type Site = Awaited<ReturnType<typeof buildSite>>

const NOT_YET_BUILT = ['/docs/'] // HA-724 deletes this once /docs/ is built

let site: Site

beforeAll(async () => {
  site = await buildSite()
})

describe.each(['index.html', '404.html'])('%s', (file: string) => {
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
      .filter((ref) => !NOT_YET_BUILT.some((prefix) => ref.startsWith(prefix)))
    expect(refs.length).toBeGreaterThan(0)
    for (const ref of refs) {
      const path = ref === '/' ? 'index.html' : ref.slice(1)
      expect(existsSync(join(site.dir, path)), ref).toBe(true)
    }
  })
})
