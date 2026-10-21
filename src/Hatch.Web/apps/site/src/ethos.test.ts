/* Scans every text file under apps/site for something true of exactly one
   installation (docs/ethos.md): a URL, a dotted-quad IP, an email, or a
   hostname-shaped token. node_modules, dist and *.test.ts are excluded - the
   test files above are where the invented values those tests need live.
   content/ is excluded too: it is long-form documentation prose, parallel to
   apps/hatch/docs/ (which this scanner never covers either), and its vendor
   reference links (install docs, a dependency's own site) are true of every
   installation by nature of being a guide, not a sign of a hardcoded address.
   A line carrying the EXEMPT_MARKER comment is exempt too - for a literal
   that is true of every installation (a pinned dependency's own vendor URL,
   not this one's), reviewed and left as plain, searchable text rather than
   split to dodge the scan. What this cannot catch is a person's name; that
   half is review's. */
/// <reference types="node" />
import { readdirSync, readFileSync } from 'node:fs'
import { join, relative } from 'node:path'
import { describe, expect, it } from 'vitest'

const ROOT = new URL('..', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1')

const EXCLUDED_DIRS = new Set(['node_modules', 'dist', 'content'])

// A reviewed, genuinely-structural literal (true of every installation, not
// this one's) can carry this comment on its own line to opt out of the scan.
export const EXEMPT_MARKER = 'ethos:allow'

const RULES: { name: string; pattern: RegExp }[] = [
  { name: 'a URL', pattern: /https?:\/\/(?!localhost\b)\S+/ },
  { name: 'a dotted-quad IP', pattern: /\b(?:\d{1,3}\.){3}\d{1,3}\b/ },
  { name: 'an email address', pattern: /[\w.+-]+@[\w-]+\.[\w.-]+/ },
  { name: 'a public hostname', pattern: /\b[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.(?:com|net|org|io|dev|app|ai)\b(?!\.)/i },
]

function listFiles(dir: string): string[] {
  const files: string[] = []
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.isDirectory()) {
      if (EXCLUDED_DIRS.has(entry.name)) continue
      files.push(...listFiles(join(dir, entry.name)))
    } else if (!entry.name.endsWith('.test.ts')) {
      files.push(join(dir, entry.name))
    }
  }
  return files
}

function lineViolations(line: string): string[] {
  if (line.includes(EXEMPT_MARKER)) return []
  return RULES.filter(({ pattern }) => pattern.test(line)).map(({ name }) => name)
}

export function violations(root: string): string[] {
  const found: string[] = []
  for (const file of listFiles(root)) {
    const lines = readFileSync(file, 'utf8').split('\n')
    for (let i = 0; i < lines.length; i++) {
      for (const name of lineViolations(lines[i])) found.push(`${relative(root, file)}:${i + 1}: ${name}`)
    }
  }
  return found
}

describe('ethos', () => {
  it('has nothing true of exactly one installation', () => {
    expect(violations(ROOT)).toEqual([])
  })
})

describe('lineViolations', () => {
  it.each([
    ['a URL', 'see https://example.com/path for more'],
    ['a dotted-quad IP', 'connect to 192.168.0.1 directly'],
    ['an email address', 'contact admin@example.com for help'],
    ['a public hostname', 'reach it at example.com today'],
  ])('flags %s', (name: string, line: string) => {
    expect(lineViolations(line)).toContain(name)
  })

  it('does not flag localhost', () => {
    expect(lineViolations('http://localhost:3000/path')).toEqual([])
  })

  it('does not flag plain prose', () => {
    expect(lineViolations('The house project tracker.')).toEqual([])
  })

  it('does not flag a line carrying the exempt marker', () => {
    expect(lineViolations(`see https://example.com/path // ${EXEMPT_MARKER}`)).toEqual([])
  })
})
