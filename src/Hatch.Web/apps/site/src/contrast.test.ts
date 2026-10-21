/* Every foreground/background pairing site.css draws a legible boundary or
   body of text with, held to WCAG's contrast floor in both themes. Parses
   tokens.css as apps/hatch/src/lib/tokens.test.ts does; the luminance math is
   restated here rather than imported from @hatch/ui, whose barrel loads
   React - a dependency this package does not carry at all. */
/// <reference types="node" />
import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

const tokensCss = readFileSync(new URL('../../../packages/ui/src/tokens.css', import.meta.url), 'utf8').replace(
  /\/\*[\s\S]*?\*\//g,
  '',
)

function declarationsOf(selectorPattern: RegExp): Record<string, string> {
  const match = selectorPattern.exec(tokensCss)
  if (!match) throw new Error(`No match for ${selectorPattern}`)
  const body = match[1]
  const props: Record<string, string> = {}
  for (const statement of body.split(';')) {
    const trimmed = statement.trim()
    if (!trimmed) continue
    const colon = trimmed.indexOf(':')
    if (colon === -1) continue
    const prop = trimmed.slice(0, colon).trim()
    const value = trimmed.slice(colon + 1).trim().replace(/\s+/g, ' ')
    props[prop] = value
  }
  return props
}

// The bare :root - light. tokens.test.ts holds the two dark blocks identical,
// so only the manual-override block is read here.
const light = declarationsOf(/:root\s*\{([^}]*)\}/)
const dark = declarationsOf(/:root\[data-theme=['"]dark['"]\]\s*\{([^}]*)\}/)

// WCAG relative luminance and contrast ratio.
function luminance(hex: string): number {
  const channels = [1, 3, 5].map((at) => parseInt(hex.slice(at, at + 2), 16) / 255)
  const [r, g, b] = channels.map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4))
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}

function contrastRatio(fg: string, bg: string): number {
  const a = luminance(fg)
  const b = luminance(bg)
  const [lighter, darker] = a > b ? [a, b] : [b, a]
  return (lighter + 0.05) / (darker + 0.05)
}

// Every foreground/background pair site.css draws with, and the floor each
// must clear: 4.5 for text, 3 for a boundary that carries meaning. Both
// grounds are opaque tokens - a translucent one is composited over --bg
// first, or named in NOT_A_PAIR instead.
const PAIRS: { fg: string; bg: string; floor: number }[] = [
  { fg: '--ink', bg: '--bg', floor: 4.5 },
  { fg: '--ink', bg: '--card', floor: 4.5 },
  { fg: '--muted', bg: '--bg', floor: 4.5 },
  { fg: '--muted', bg: '--card', floor: 4.5 }, // tightest margin: 4.83:1 dark
  { fg: '--primary-ink', bg: '--bg', floor: 4.5 },
  { fg: '--primary-ink', bg: '--card', floor: 4.5 },
  { fg: '--on-accent', bg: '--primary', floor: 4.5 },
  { fg: '--primary', bg: '--bg', floor: 3 }, // the focus ring
  { fg: '--primary', bg: '--card', floor: 3 }, // an agent's column stroke
]

// Referenced by site.css but not a resting foreground/background pair.
const NOT_A_PAIR: Record<string, string> = {
  '--line': 'a hairline card/pre/header divider, decorative only',
  '--line-strong': 'a docs blockquote rule, decorative only',
  '--primary-bg': 'a translucent hover wash over --bg, not a resting pair',
}

describe.each([
  ['light', light],
  ['dark', dark],
] as const)('contrast, %s', (_name, declarations) => {
  it.each(PAIRS)('$fg on $bg clears $floor:1', ({ fg, bg, floor }) => {
    const ratio = contrastRatio(declarations[fg], declarations[bg])
    expect(ratio).toBeGreaterThanOrEqual(floor)
  })
})

describe('site.css color tokens', () => {
  const siteCss = readFileSync(new URL('./site.css', import.meta.url), 'utf8')
  // --measure*, --r*, --sp-* and --t-* are layout and type tokens, not
  // colours, and are out of scope for this test.
  const NOT_A_COLOR = /^--(?:measure|r|sp|t)(?:-|$)/
  const referenced = [...new Set([...siteCss.matchAll(/var\((--[a-z0-9-]+)/g)].map((match) => match[1]))].filter(
    (token) => !NOT_A_COLOR.test(token),
  )

  it('names every colour token it references, in PAIRS or NOT_A_PAIR', () => {
    const named = new Set([...PAIRS.flatMap((pair) => [pair.fg, pair.bg]), ...Object.keys(NOT_A_PAIR)])
    expect(referenced.length).toBeGreaterThan(0)
    for (const token of referenced) {
      expect(named.has(token), token).toBe(true)
    }
  })
})
