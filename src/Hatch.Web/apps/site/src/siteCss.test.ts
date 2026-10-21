/* The page frame states no value of its own: every colour, length and type
   step comes from @hatch/ui's tokens, so day and night both work with no
   second rule. Read as text, the way docsCss.test.ts reads App.css: there is
   no DOM in this runner. */
/// <reference types="node" />
import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

const stripComments = (text: string): string => text.replace(/\/\*[\s\S]*?\*\//g, '')

// base.css: "below the base, hairlines and 1-3px nudges are not tokens", and
// site.css: "the 1px hairline is the one exception".
const HAIRLINE = /(?<![\d.])1px\b/g

const rulesOf = (css: string): { selectors: string; body: string }[] =>
  [...stripComments(css).matchAll(/([^{}]+)\{([^{}]*)\}/g)].map(([, selectors, body]) => ({
    selectors: selectors.trim(),
    body,
  }))

export function violations(css: string): string[] {
  const found: string[] = []
  for (const { selectors, body } of rulesOf(css)) {
    const bare = body.replace(/var\([^)]*\)/g, '').replace(HAIRLINE, '')
    if (/#[0-9a-f]{3,8}\b/i.test(bare)) found.push(`${selectors}: a hex colour`)
    if (/\b(rgba?|hsla?)\(/i.test(bare)) found.push(`${selectors}: an rgb()/hsl() colour`)
    if (/\d(\.\d+)?px/.test(bare)) found.push(`${selectors}: a px length other than the hairline`)
  }
  return found
}

const siteCss = readFileSync(new URL('./site.css', import.meta.url), 'utf8')

describe('site.css', () => {
  it('has rules, and no violation', () => {
    expect(rulesOf(siteCss).length).toBeGreaterThan(0)
    expect(violations(siteCss)).toEqual([])
  })

  it('writes every @media prelude in rem', () => {
    expect(stripComments(siteCss)).not.toMatch(/@media[^{]*\d+px/)
  })

  it('gives every grid track minmax(0, …), so a long word cannot widen the page', () => {
    const columns = [...stripComments(siteCss).matchAll(/grid-template-columns:([^;}]*)/g)].map(([, value]) => value)
    expect(columns.length).toBeGreaterThan(0)
    for (const value of columns) expect(value).toMatch(/^\s*(repeat\(\d+,\s*)?minmax\(0,/)
  })

  it('lets the install block wrap anywhere', () => {
    const pre = rulesOf(siteCss).find(({ selectors }) => selectors.endsWith(' pre'))
    expect(pre?.body).toMatch(/overflow-wrap:\s*anywhere/)
  })
})

describe('violations', () => {
  it.each([
    ['a hex colour', 'a{color:#fff}'],
    ['an rgb colour', 'a{color:rgb(0,0,0)}'],
    ['an hsl colour', 'a{background:hsl(0 0% 0%)}'],
    ['a px length', 'a{padding:4px}'],
    ['a fractional px length', 'a{padding:0.5px}'],
    ['a two-digit px length', 'a{padding:11px}'],
  ])('flags %s', (_name: string, css: string) => {
    expect(violations(css)).toHaveLength(1)
  })

  it.each([
    ['the hairline', 'a{border:1px solid var(--line)}'],
    ['a spacing token', 'a{padding:var(--sp-4)}'],
    ['a px inside var()', 'a{padding:var(--sp-4, 4px)}'],
  ])('does not flag %s', (_name: string, css: string) => {
    expect(violations(css)).toEqual([])
  })
})

describe.each(['index.html', '404.html'])('%s source', (file: string) => {
  const html = readFileSync(new URL(`../${file}`, import.meta.url), 'utf8').replace(/<!--[\s\S]*?-->/g, '')

  it('carries no style attribute, no style element, no colour and no data-theme', () => {
    expect(html).not.toMatch(/\sstyle=/)
    expect(html).not.toMatch(/<style/)
    expect(html).not.toMatch(/(?<![&\w])#[0-9a-f]{3,8}\b/i)
    expect(html).not.toMatch(/data-theme/)
  })
})
