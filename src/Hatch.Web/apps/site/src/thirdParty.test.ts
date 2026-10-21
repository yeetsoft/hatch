/* Nothing leaves the page: the built home and 404 pages, their CSS and their
   JavaScript make no request to a host that is not this site. Regex over the
   built text: there is no DOM in this runner. */
/// <reference types="node" />
import { readFileSync, readdirSync } from 'node:fs'
import { join } from 'node:path'
import { beforeAll, describe, expect, it } from 'vitest'
import { buildSite } from './testing/buildSite'
import { attr, tagsOf, withoutComments } from './testing/html'

type Site = Awaited<ReturnType<typeof buildSite>>

const ABSOLUTE_URL = /https?:\/\//
// A bare `//` can turn up in the inlined font's base64 body, so only a `//`
// that opens a url(), a quoted value or an attribute value counts.
const PROTOCOL_RELATIVE = /(?:url\(|["'=])\/\//
const SAME_ORIGIN_SRC = /^(?:\/(?!\/)|\.\.?\/|[^:/]+$)/

let site: Site
let assets: string[]

const assetsEnding = (suffix: string): string[] => assets.filter((f) => f.endsWith(suffix))
const readAsset = (file: string): string => readFileSync(join(site.dir, 'assets', file), 'utf8')

beforeAll(async () => {
  site = await buildSite()
  assets = readdirSync(join(site.dir, 'assets'))
})

describe.each(['index.html', '404.html'])('%s', (file: string) => {
  let html: string
  beforeAll(() => {
    html = withoutComments(site.read(file))
  })

  it('names no absolute or protocol-relative URL', () => {
    expect(html).not.toMatch(ABSOLUTE_URL)
    expect(html).not.toMatch(PROTOCOL_RELATIVE)
  })

  it('has at most two scripts, each a module from this origin', () => {
    const scripts = tagsOf(html, 'script')
    expect(scripts.length).toBeLessThanOrEqual(2)
    for (const tag of scripts) {
      expect(attr(tag, 'type'), tag).toBe('module')
      expect(attr(tag, 'src') ?? '', tag).toMatch(SAME_ORIGIN_SRC)
    }
  })

  it('embeds nothing and posts nothing', () => {
    expect(tagsOf(html, 'iframe')).toEqual([])
    expect(tagsOf(html, 'form')).toEqual([])
  })

  it('opens no connection ahead of a request', () => {
    const hints = tagsOf(html, 'link').filter((t) => /^(?:preconnect|dns-prefetch)$/.test(attr(t, 'rel') ?? ''))
    expect(hints).toEqual([])
  })
})

describe('the built assets', () => {
  it('has CSS that names no absolute or protocol-relative URL', () => {
    const sheets = assetsEnding('.css')
    expect(sheets.length).toBeGreaterThan(0)
    for (const sheet of sheets) {
      const css = readAsset(sheet)
      expect(css, sheet).not.toMatch(ABSOLUTE_URL)
      expect(css, sheet).not.toMatch(PROTOCOL_RELATIVE)
    }
  })

  it('has JavaScript that names no absolute URL and brings no React or network call', () => {
    const scripts = assetsEnding('.js')
    expect(scripts.length).toBeGreaterThan(0)
    for (const script of scripts) {
      const js = readAsset(script)
      expect(js, script).not.toMatch(ABSOLUTE_URL)
      expect(js, script).not.toMatch(/react-dom|jsx-runtime/)
      expect(js, script).not.toMatch(/\bfetch\(|XMLHttpRequest/)
    }
  })

  it('holds the self-hosted font', () => {
    expect(assetsEnding('.woff2').length).toBeGreaterThan(0)
  })
})
