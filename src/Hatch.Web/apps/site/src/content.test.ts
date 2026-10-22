/* setup/index.html, built through vite-plugin-content.ts: one page, no docs
   sidebar, outline or pager, every fenced block carried over byte for byte
   except the one clone-url substitution, and the 15 numbered steps in order.
   Regex over the built text, the same as docs.test.ts: there is no DOM in
   this runner. */
/// <reference types="node" />
import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { marked, type Token, type Tokens } from 'marked'
import { beforeAll, describe, expect, it } from 'vitest'
import { escapeHtml } from '../vite-plugin-site'
import { buildSite } from './testing/buildSite'
import { tagsOf, withoutComments } from './testing/html'

type Site = Awaited<ReturnType<typeof buildSite>>

const CONTENT_PATH = join(dirname(fileURLToPath(import.meta.url)), '../content/setup.md')
const source = readFileSync(CONTENT_PATH, 'utf8')

function frontmatterTitle(raw: string): string {
  const lines = raw.split(/\r?\n/)
  const close = lines.indexOf('---', 1)
  const line = lines.slice(1, close).find((l) => l.startsWith('title:'))
  if (!line) throw new Error('fixture has no title: line')
  return line.slice('title:'.length).trim()
}

/** Every fenced code block's raw text, in document order, including one
    nested inside a blockquote - the same segmentation `marked` itself uses to
    render them, so this cannot disagree with the plugin's own parser. */
function fencedBlocks(markdown: string): string[] {
  const blocks: string[] = []
  const walk = (tokens: Token[]): void => {
    for (const token of tokens) {
      if (token.type === 'code') {
        blocks.push((token as Tokens.Code).text)
        continue
      }
      if ('tokens' in token && Array.isArray(token.tokens)) walk(token.tokens as Token[])
      if ('items' in token && Array.isArray((token as Tokens.List).items)) walk((token as Tokens.List).items as Token[])
    }
  }
  walk(marked.lexer(markdown))
  return blocks
}

const decodeEntities = (s: string): string =>
  s.replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&amp;/g, '&')

/** Every `<pre>`'s text, in document order, with its nested `<code>` tag
    stripped, its entities decoded, and the trailing newline `marked` always
    puts before `</code>` removed - so this lines up with a `Tokens.Code`'s own
    `text`, which carries no such newline. */
function preTexts(html: string): string[] {
  return [...html.matchAll(/<pre\b[^>]*>([\s\S]*?)<\/pre>/g)].map((m) =>
    decodeEntities(m[1].replace(/<\/?code\b[^>]*>/g, '')).replace(/\n$/, ''),
  )
}

const STEP_HEADING = /^### (\d+\. .+)$/gm

let site: Site
let html: string

beforeAll(async () => {
  site = await buildSite()
  html = withoutComments(site.read('setup/index.html'))
})

describe('setup/index.html', () => {
  it('has exactly one <h1>, equal to the frontmatter title', () => {
    expect(tagsOf(html, 'h1')).toHaveLength(1)
    expect(html).toContain(`<h1>${escapeHtml(frontmatterTitle(source))}</h1>`)
  })

  it('carries no docs sidebar, outline or pager', () => {
    expect(html).not.toContain('site-docs-sidebar')
    expect(html).not.toContain('site-docs-outline')
    expect(html).not.toContain('site-docs-pager')
  })

  it('lists the 15 numbered steps as headings, in order', () => {
    const expected = [...source.matchAll(STEP_HEADING)].map((m) => m[1])
    expect(expected).toHaveLength(15)
    const headings = [...html.matchAll(/<h3\b[^>]*>([\s\S]*?)<a class="hatch-docs-anchor"/g)].map((m) =>
      decodeEntities(m[1]),
    )
    expect(headings.filter((h) => /^\d+\. /.test(h))).toEqual(expected)
  })

  it('carries every fenced block byte for byte, except the clone-url one', () => {
    const sourceBlocks = fencedBlocks(source)
    const builtBlocks = preTexts(html)
    expect(builtBlocks).toHaveLength(sourceBlocks.length)
    const cloneIndex = sourceBlocks.findIndex((b) => b.includes('{{clone-url}}'))
    expect(cloneIndex).toBeGreaterThanOrEqual(0)
    for (let i = 0; i < sourceBlocks.length; i++) {
      if (i === cloneIndex) continue
      expect(builtBlocks[i], `block ${i}`).toBe(sourceBlocks[i])
    }
  })

  it('substitutes the clone url, or the fallback text when unset', async () => {
    const withRepo = await buildSite({ GITHUB_SERVER_URL: 'https://github.test', GITHUB_REPOSITORY: 'o/r' })
    const withRepoHtml = withoutComments(withRepo.read('setup/index.html'))
    expect(preTexts(withRepoHtml)).toContain('cd "$HOME" && git clone https://github.test/o/r.git hatch')

    const withoutRepo = await buildSite({})
    const withoutRepoHtml = withoutComments(withoutRepo.read('setup/index.html'))
    expect(preTexts(withoutRepoHtml)).toContain('cd "$HOME" && git clone <the repository\'s clone URL> hatch')
  })

  it('substitutes its own address when base_url is set', async () => {
    const withOrigin = await buildSite({ base_url: 'https://example.invalid/' })
    const withOriginHtml = withoutComments(withOrigin.read('setup/index.html'))
    const pageAddress = 'https://example.invalid/setup/'

    expect(preTexts(withOriginHtml)).toContain(`Set me up with Hatch: ${pageAddress}`)
    expect(preTexts(withOriginHtml)).toContain(`curl -fsSL ${pageAddress}`)
    expect(preTexts(withOriginHtml)).toContain(
      `Invoke-WebRequest -UseBasicParsing ${pageAddress} | Select-Object -ExpandProperty Content`,
    )
    expect(decodeEntities(withOriginHtml)).toContain(`<code>irm ${pageAddress}</code> in PowerShell 7.`)
    expect(withOriginHtml).not.toContain('the address of this page')
  })
})
