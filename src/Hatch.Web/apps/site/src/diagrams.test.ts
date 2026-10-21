/* The generic rules every <svg> inside a .site-figure holds to, whatever it
   draws: accessible by itself, styled by class rather than attribute, and
   sized to the frame it sits in. Regex over the built text: there is no DOM
   in this runner. */
/// <reference types="node" />
import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import { buildSite } from './testing/buildSite'
import { attr, attrValues, ids, withoutComments } from './testing/html'

// 360px frame minus two --sp-4 paddings minus two 1px hairlines.
const NARROW_MAX = 326
// 64rem (1024px) frame minus two --sp-8 paddings minus two 1px hairlines.
const WIDE_MAX = 958

const LAYOUT_CLASSES = ['site-diagram-narrow', 'site-diagram-wide']

// The lookbehind siteCss.test.ts's violations() uses, so a URL fragment
// ("#start-here") or an HTML entity ("&#39;") is not mistaken for a colour.
const HEX_COLOUR = /(?<![&\w])#[0-9a-f]{3,8}\b/i

export const figuresOf = (html: string): string[] => html.match(/<figure class="site-figure"[\s\S]*?<\/figure>/g) ?? []

export const svgsOf = (html: string): string[] => html.match(/<svg\b[\s\S]*?<\/svg>/g) ?? []

export const textsOf = (svg: string): string[] =>
  [...svg.matchAll(/<text\b[^>]*>([\s\S]*?)<\/text>/g)].map((match) =>
    match[1]
      .replace(/<[^>]*>/g, '')
      .replace(/\s+/g, ' ')
      .trim(),
  )

const openingTagOf = (svg: string): string => svg.match(/^<svg\b[^>]*>/)?.[0] ?? ''

const layoutClassesOf = (svg: string): string[] => {
  const classes = (attr(openingTagOf(svg), 'class') ?? '').split(/\s+/)
  return LAYOUT_CLASSES.filter((name) => classes.includes(name))
}

export function violations(svg: string): string[] {
  const found: string[] = []
  const opening = openingTagOf(svg)
  const body = svg.slice(opening.length, svg.length - '</svg>'.length)

  if (attr(opening, 'role') !== 'img') found.push('role is not "img"')

  const titleMatch = body.match(/^\s*<title id="([^"]+)">([^<]*)<\/title>/)
  const afterTitle = body.slice((titleMatch?.index ?? 0) + (titleMatch?.[0].length ?? 0))
  const descMatch = afterTitle.match(/^\s*<desc id="([^"]+)">([^<]*)<\/desc>/)

  if (!titleMatch || !titleMatch[2].trim()) found.push('first child is not a non-empty <title id>')
  if (!descMatch || !descMatch[2].trim()) found.push('second child is not a non-empty <desc id>')

  const labelledby = attr(opening, 'aria-labelledby')
  const expectedLabelledby = titleMatch && descMatch ? `${titleMatch[1]} ${descMatch[1]}` : undefined
  if (labelledby === undefined || labelledby !== expectedLabelledby)
    found.push('aria-labelledby does not name the title then the desc, in order')

  if (/\sxmlns(:\w+)?=/.test(opening)) found.push('an xmlns attribute')
  if (/\sxlink:href=/.test(svg)) found.push('an xlink:href attribute')
  if (/https?:\/\//.test(svg)) found.push('an http(s) URL')
  if (/\sstyle=/.test(svg)) found.push('a style attribute')
  if (/\s(?:font|font-size|font-family)=/.test(svg)) found.push('a font attribute')
  if (HEX_COLOUR.test(svg)) found.push('a hex colour')
  if (/\b(?:rgb|hsl)\(/i.test(svg)) found.push('an rgb()/hsl() colour')

  for (const value of [...attrValues(svg, 'fill'), ...attrValues(svg, 'stroke')])
    if (value !== 'none' && value !== 'currentColor') found.push(`fill/stroke is "${value}", not none or currentColor`)

  const layoutClasses = layoutClassesOf(svg)
  if (layoutClasses.length !== 1) found.push('does not carry exactly one of site-diagram-narrow/site-diagram-wide')
  const narrow = layoutClasses[0] === 'site-diagram-narrow'
  const wide = layoutClasses[0] === 'site-diagram-wide'

  const viewBox = (attr(opening, 'viewBox') ?? '').trim().split(/\s+/).map(Number)
  if (viewBox.length !== 4 || viewBox.some((n) => Number.isNaN(n))) {
    found.push('viewBox is missing or malformed')
  } else {
    const [, , viewBoxWidth, viewBoxHeight] = viewBox
    if (attr(opening, 'width') !== String(viewBoxWidth)) found.push('width does not equal the viewBox width')
    if (attr(opening, 'height') !== String(viewBoxHeight)) found.push('height does not equal the viewBox height')
    if (narrow && viewBoxWidth > NARROW_MAX) found.push(`narrow viewBox is wider than ${NARROW_MAX}`)
    if (wide && viewBoxWidth > WIDE_MAX) found.push(`wide viewBox is wider than ${WIDE_MAX}`)
  }

  return found
}

const GOOD_SVG =
  '<svg class="site-diagram-narrow" role="img" aria-labelledby="t-title t-desc" viewBox="0 0 100 50" width="100" height="50">' +
  '<title id="t-title">A title</title>' +
  '<desc id="t-desc">A description</desc>' +
  '<g><rect class="site-diagram-node" /></g>' +
  '</svg>'

describe('violations', () => {
  it('yields nothing for a minimal good svg', () => {
    expect(violations(GOOD_SVG)).toEqual([])
  })

  it.each([
    ['no role', GOOD_SVG.replace('role="img" ', '')],
    ['missing title', GOOD_SVG.replace('<title id="t-title">A title</title>', '')],
    ['aria-labelledby mismatch', GOOD_SVG.replace('aria-labelledby="t-title t-desc"', 'aria-labelledby="t-desc t-title"')],
    ['xmlns', GOOD_SVG.replace('<svg class=', '<svg xmlns="http://www.w3.org/2000/svg" class=')],
    ['a style attribute', GOOD_SVG.replace('<rect class="site-diagram-node" />', '<rect class="site-diagram-node" style="fill:red" />')],
    ['a font-size attribute', GOOD_SVG.replace('<rect class="site-diagram-node" />', '<rect class="site-diagram-node" font-size="12" />')],
    ['a hex colour', GOOD_SVG.replace('<rect class="site-diagram-node" />', '<rect fill="#fff" />')],
    ['an rgb colour', GOOD_SVG.replace('<rect class="site-diagram-node" />', '<rect fill="rgb(0,0,0)" />')],
    ['fill="red"', GOOD_SVG.replace('<rect class="site-diagram-node" />', '<rect fill="red" />')],
    ['both layout classes', GOOD_SVG.replace('class="site-diagram-narrow"', 'class="site-diagram-narrow site-diagram-wide"')],
    ['neither layout class', GOOD_SVG.replace('class="site-diagram-narrow" ', '')],
    ['width not equal to viewBox', GOOD_SVG.replace('width="100"', 'width="999"')],
    ['narrow wider than the max', GOOD_SVG.replace('viewBox="0 0 100 50" width="100" height="50"', 'viewBox="0 0 400 50" width="400" height="50"')],
    [
      'wide wider than the max',
      GOOD_SVG.replace('class="site-diagram-narrow"', 'class="site-diagram-wide"').replace(
        'viewBox="0 0 100 50" width="100" height="50"',
        'viewBox="0 0 1000 50" width="1000" height="50"',
      ),
    ],
  ])('flags %s', (_name: string, svg: string) => {
    expect(violations(svg).length).toBeGreaterThanOrEqual(1)
  })
})

const page = withoutComments((await buildSite()).read('index.html'))

describe('every <svg> inside a .site-figure', () => {
  const svgsInFigures = figuresOf(page).flatMap(svgsOf)

  it('has at least two svgs to check, so this is not vacuous', () => {
    expect(svgsInFigures.length).toBeGreaterThanOrEqual(2)
  })

  it.each(svgsInFigures.map((svg) => [`${attr(openingTagOf(svg), 'class')} ${ids(svg)[0]}`, svg] as const))(
    '%s holds every rule',
    (_label, svg) => {
      expect(violations(svg)).toEqual([])
    },
  )

  it('is unique in id, across the whole page', () => {
    const pageIds = ids(page)
    expect(new Set(pageIds).size).toBe(pageIds.length)
  })

  const figuresWithSvgs = figuresOf(page).filter((figure) => svgsOf(figure).length > 0)

  it('has at least one figure with a drawing to check', () => {
    expect(figuresWithSvgs.length).toBeGreaterThan(0)
  })

  it.each(figuresWithSvgs.map((figure) => [attr(figure, 'data-diagram') ?? '', figure] as const))(
    'holds exactly one narrow and one wide svg: %s',
    (_name, figure) => {
      const svgs = svgsOf(figure)
      expect(svgs.filter((svg) => layoutClassesOf(svg).includes('site-diagram-narrow'))).toHaveLength(1)
      expect(svgs.filter((svg) => layoutClassesOf(svg).includes('site-diagram-wide'))).toHaveLength(1)
    },
  )
})

describe('site.css', () => {
  const css = readFileSync(new URL('./site.css', import.meta.url), 'utf8')

  const mediaBlockBodyOf = (text: string, prelude: string): string => {
    const open = text.indexOf('{', text.indexOf(prelude))
    let depth = 0
    for (let i = open; i < text.length; i++) {
      if (text[i] === '{') depth++
      else if (text[i] === '}' && --depth === 0) return text.slice(open + 1, i)
    }
    return ''
  }

  it('hides .site-figure .site-diagram-wide by default', () => {
    const beforeFirstMedia = css.slice(0, css.indexOf('@media'))
    const rule = beforeFirstMedia.match(/\.site-figure \.site-diagram-wide\s*\{([^}]*)\}/)
    expect(rule?.[1]).toMatch(/display:\s*none/)
  })

  it('shows the wide drawing and hides the narrow one from 64rem', () => {
    const body = mediaBlockBodyOf(css, '@media (min-width: 64rem)')
    expect(body).toMatch(/\.site-figure \.site-diagram-narrow\s*\{[^}]*display:\s*none[^}]*\}/)
    expect(body).toMatch(/\.site-figure \.site-diagram-wide\s*\{[^}]*display:\s*block[^}]*\}/)
  })
})

describe('the moving parts', () => {
  const figure = figuresOf(page).find((f) => attr(f, 'data-diagram') === 'moving-parts')
  const svgs = svgsOf(figure ?? '')

  it('has a figure with exactly two svgs', () => {
    expect(svgs).toHaveLength(2)
  })

  const NODES = ['you', 'the board', 'the runner', 'a Claude Code session', 'your repository on the forge']
  const EDGES = ['ticket', 'dispatch', 'increment', 'pull request', 'review', 'merge']

  it.each(svgs.map((svg) => [attr(openingTagOf(svg), 'class') ?? '', svg] as const))(
    '%s holds the moving-parts content',
    (_class, svg) => {
      const texts = textsOf(svg)
      const joined = texts.map((t, i) => (texts[i + 1] ? `${t} ${texts[i + 1]}` : t))

      for (const node of NODES) expect(texts.includes(node) || joined.includes(node)).toBe(true)

      expect(texts.filter((t) => EDGES.includes(t))).toEqual(EDGES)

      expect(svg.match(/<desc\b[^>]*>([\s\S]*?)<\/desc>/)?.[1]).toContain('merge train')
    },
  )
})

describe('the life of a ticket', () => {
  const figure = figuresOf(page).find((f) => attr(f, 'data-diagram') === 'life-of-a-ticket')
  const svgs = svgsOf(figure ?? '')

  const OWNER_WORDS: Record<string, string> = { operator: 'you', agent: 'agent' }

  const lines = readFileSync(new URL('../../../../../docs/hatch.md', import.meta.url), 'utf8').split(/\r?\n/)
  const header = lines.findIndex((line) => line.startsWith('| Column | Sort |'))
  const rows: string[] = []
  for (let i = header + 2; header >= 0 && i < lines.length && lines[i].startsWith('|'); i++) rows.push(lines[i])
  const columns = rows.map((row) => {
    const cells = row.split('|')
    const whose = cells[5].split(';')[0].replace(/\*/g, '').trim()
    const owner = OWNER_WORDS[whose]
    if (owner === undefined) throw new Error(`the Status table names an owner this test does not know: "${whose}"`)
    return { name: cells[1].trim(), owner }
  })

  it('reads seven stock columns from the docs', () => {
    expect(columns).toHaveLength(7)
  })

  it('has a figure with exactly two svgs', () => {
    expect(svgs).toHaveLength(2)
  })

  it.each(svgs.map((svg) => [attr(openingTagOf(svg), 'class') ?? '', svg] as const))(
    '%s holds the stock columns and their owners',
    (_class, svg) => {
      const texts = textsOf(svg)

      const at = columns.map((column) => texts.indexOf(column.name))
      columns.forEach((column, i) => {
        expect(at[i], `${column.name} is an entry`).toBeGreaterThanOrEqual(0)
        expect(texts[at[i] + 1], `the owner after ${column.name}`).toBe(column.owner)
      })
      expect(at, 'the columns appear in docs order').toEqual([...at].sort((a, b) => a - b))

      const approved = texts.indexOf('Approved')
      expect(approved, 'Approved sits between In Review and Done').toBeGreaterThan(texts.indexOf('In Review'))
      expect(approved).toBeLessThan(texts.indexOf('Done'))

      for (const note of [/optional/, /you add/, /merge train/, /no session/])
        expect(texts.filter((t) => note.test(t)), `one entry matching ${note}`).toHaveLength(1)

      expect(texts.some((t) => t.includes('conflict'))).toBe(true)
      expect(texts.some((t) => t.includes('failed build'))).toBe(true)

      const desc = svg.match(/<desc\b[^>]*>([\s\S]*?)<\/desc>/)?.[1]
      expect(desc).toContain('merge train')
      expect(desc).toContain('reaches Done')
    },
  )
})
