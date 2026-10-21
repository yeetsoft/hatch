/* Nothing leaves the page: the built home and 404 pages, their CSS and their
   JavaScript make no request to a host that is not this site. Regex over the
   built text: there is no DOM in this runner. */
/// <reference types="node" />
import { readFileSync, readdirSync } from 'node:fs'
import { join } from 'node:path'
import { beforeAll, describe, expect, it } from 'vitest'
import { buildSite } from './testing/buildSite'
import { attr, tagsOf, withoutComments } from './testing/html'
import { isAllowedMermaidUrl, reachable } from './lib/mermaidChunkGraph'

type Site = Awaited<ReturnType<typeof buildSite>>

const ABSOLUTE_URL = /https?:\/\//
// The full text of each absolute URL a script names, so one can be checked
// against isAllowedMermaidUrl rather than merely detected.
const ABSOLUTE_URL_TEXT = /https?:\/\/[^\s"'`)]*/g
// A bare `//` can turn up in the inlined font's base64 body, so only a `//`
// that opens a url(), a quoted value or an attribute value counts.
const PROTOCOL_RELATIVE = /(?:url\(|["'=])\/\//
const SAME_ORIGIN_SRC = /^(?:\/(?!\/)|\.\.?\/|[^:/]+$)/

// The rendered markdown inside a docs page is the repository's own committed
// prose (HA-1219's "no sanitiser" call) and may link out by design - cli.github.com,
// a localhost example, and so on. The leak this file guards against is the frame
// loading a third-party resource, not a doc page's own anchor text, so the
// body renderDocs() produced is cut out before the URL checks run.
const withoutDocsBody = (html: string): string => html.replace(/<article class="hatch-docs-body">[\s\S]*?<\/article>/, '')

let site: Site
let assets: string[]

const assetsEnding = (suffix: string): string[] => assets.filter((f) => f.endsWith(suffix))
const readAsset = (file: string): string => readFileSync(join(site.dir, 'assets', file), 'utf8')

beforeAll(async () => {
  site = await buildSite()
  assets = readdirSync(join(site.dir, 'assets'))
})

describe.each(['index.html', '404.html', 'docs/index.html', 'docs/getting-started/index.html'])('%s', (file: string) => {
  let html: string
  beforeAll(() => {
    html = withoutComments(site.read(file))
  })

  it('names no absolute or protocol-relative URL outside a doc\'s own body', () => {
    const frame = withoutDocsBody(html)
    expect(frame).not.toMatch(ABSOLUTE_URL)
    expect(frame).not.toMatch(PROTOCOL_RELATIVE)
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

  it('has JavaScript that names only allowed absolute URLs, and brings no React or network call', () => {
    const chunks = site.output.filter((chunk) => chunk.type === 'chunk')
    const facade = chunks.find((chunk) => chunk.moduleIds.some((id) => id.endsWith('src/lib/mermaid.ts')))!
    const mermaidSubtree = reachable(chunks, [facade.fileName])
    // docsSearchField.ts (HA-1280) is the one script allowed a real fetch(): it
    // reads HA-1279's own index, at a path pinned below, and nothing else in
    // its chunk graph calls fetch at all - so one call site in the subtree,
    // naming that path somewhere in the same chunk, is what "pinned" means here.
    const searchEntry = chunks.find((chunk) => chunk.moduleIds.some((id) => id.endsWith('src/docsSearchField.ts')))!
    const searchSubtree = reachable(chunks, [searchEntry.fileName])

    const scripts = assetsEnding('.js')
    expect(scripts.length).toBeGreaterThan(0)
    for (const script of scripts) {
      const js = readAsset(script)
      const urls = js.match(ABSOLUTE_URL_TEXT) ?? []
      if (mermaidSubtree.has(`assets/${script}`)) {
        for (const url of urls) expect(isAllowedMermaidUrl(url), `${script}: ${url}`).toBe(true)
      } else {
        expect(js, script).not.toMatch(ABSOLUTE_URL)
      }
      expect(js, script).not.toMatch(/react-dom|jsx-runtime/)
      expect(js, script).not.toMatch(/XMLHttpRequest/)
      // A real `fetch()` call always carries an argument - katex (one of
      // mermaid's dependencies) names its own LaTeX token-reader method
      // `fetch()` on its Parser class, both as `this.fetch()` calls and as
      // the method's own definition (`fetch(){...}`, indistinguishable from
      // a call once minified), and every occurrence of either is
      // zero-argument, so this needs no exception by chunk name there.
      if (searchSubtree.has(`assets/${script}`)) {
        expect(js.match(/\bfetch\(/g) ?? [], script).toHaveLength(1)
        expect(js, script).toMatch('/docs/search-index.json')
      } else {
        expect(js, script).not.toMatch(/\bfetch\([^)]/)
      }
    }
  })

  it('holds the self-hosted font', () => {
    expect(assetsEnding('.woff2').length).toBeGreaterThan(0)
  })
})

describe('isAllowedMermaidUrl', () => {
  it('rejects a url outside every allow-listed prefix', () => {
    expect(isAllowedMermaidUrl('https://evil.example/')).toBe(false)
  })

  it('allows a url inside each allow-listed prefix', () => {
    const allowed = [
      'http://www.w3.org/2000/svg',
      'http://www.eclipse.org/elk/',
      'http:///org/eclipse/elk/core',
      'https://chevrotain.io/docs/',
      'https://github.com/chevrotain/chevrotain/issues/1',
      'https://en.wikipedia.org/wiki/LL_parser',
      'https://github.com/markedjs/marked',
      'https://github.com/mermaid-js/mermaid/releases',
      'https://langium.org/docs/',
      'https://rolldown.rs/guide/',
    ]
    for (const url of allowed) expect(isAllowedMermaidUrl(url), url).toBe(true)
  })

  it('allows the bare http:// literal, but not anything appended to it', () => {
    expect(isAllowedMermaidUrl('http://')).toBe(true)
    expect(isAllowedMermaidUrl('http://evil.example/')).toBe(false)
  })
})
