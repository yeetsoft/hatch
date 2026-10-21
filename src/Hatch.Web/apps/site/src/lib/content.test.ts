import { describe, expect, it } from 'vitest'
import { parseContentPage } from './content'

/** A minimal content page, as `page()` in `apps/hatch/src/lib/docs.test.ts` does. */
function page(fields: Record<string, string | undefined> = {}, body = 'Body.\n'): string {
  const all = { title: 'T', summary: 'S', ...fields }
  const lines = Object.entries(all)
    .filter(([, v]) => v !== undefined)
    .map(([k, v]) => `${k}: ${v}`)
  return ['---', ...lines, '---', body].join('\n')
}

describe('parseContentPage', () => {
  const path = '../../content/x.md'

  it('reads the fields, and the slug from the path', () => {
    const raw = page({ title: 'Set up Hatch', summary: 'How to install it' })
    expect(parseContentPage('../../content/setup.md', raw)).toEqual({
      slug: 'setup',
      title: 'Set up Hatch',
      summary: 'How to install it',
      body: 'Body.\n',
    })
  })

  it('accepts CRLF', () => {
    const raw = page()
    const crlf = parseContentPage(path, raw.replace(/\n/g, '\r\n'))
    expect(crlf).toEqual(parseContentPage(path, raw))
    expect(crlf.body).not.toContain('\r')
  })

  it('keeps a colon in the summary', () => {
    expect(parseContentPage(path, page({ summary: 'Use it: or not' })).summary).toBe('Use it: or not')
  })

  it('starts the body after the closing fence, dropping leading blank lines', () => {
    const { body } = parseContentPage(path, page({}, '\n\n# Heading\n\ntext\n'))
    expect(body).toBe('# Heading\n\ntext\n')
    expect(body).not.toContain('title:')
    expect(body).not.toContain('---')
  })

  it.each(['title', 'summary'])('throws, naming the file, when %s is missing', (key) => {
    expect(() => parseContentPage(path, page({ [key]: undefined }))).toThrow('x.md')
    expect(() => parseContentPage(path, page({ [key]: undefined }))).toThrow(`\`${key}\``)
  })

  it('throws on an unknown key', () => {
    expect(() => parseContentPage(path, page({ author: 'me' }))).toThrow('x.md')
  })

  it('throws on a repeated key', () => {
    const raw = page().replace('---\n', '---\ntitle: Again\n')
    expect(() => parseContentPage(path, raw)).toThrow('x.md')
  })

  it('throws on an empty value', () => {
    expect(() => parseContentPage(path, page({ summary: '' }))).toThrow('x.md')
  })

  it('throws on a line without a colon', () => {
    const raw = page().replace('---\n', '---\nnot a field\n')
    expect(() => parseContentPage(path, raw)).toThrow('x.md')
  })

  it('throws without an opening fence', () => {
    expect(() => parseContentPage(path, '# Just a heading\n')).toThrow('x.md')
  })

  it('throws without a closing fence', () => {
    expect(() => parseContentPage(path, '---\ntitle: T\nsummary: S\n')).toThrow('x.md')
  })

  it.each(['X.md', 'a b.md'])('throws on the slug in %s', (file) => {
    expect(() => parseContentPage(`../../content/${file}`, page())).toThrow(file)
  })

  it('throws on a path that is not .md', () => {
    expect(() => parseContentPage('../../content/x.txt', page())).toThrow('x.txt')
  })
})
