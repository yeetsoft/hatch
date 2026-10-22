/* Scans every text file under apps/site for something true of exactly one
   installation (docs/ethos.md): a URL, a dotted-quad IP, an email, a
   hostname-shaped token, or a www. host. node_modules, dist and *.test.ts are
   excluded - the test files above are where the invented values those tests
   need live.
   content/ is scanned too, under a different rule set: it is guide prose that
   names vendors (install docs, a dependency's own site), so a host there must
   equal or end in an entry of THIRD_PARTY_DOMAINS, and anything else - the
   site's own hostname included - fails. The workflow files under
   .github/workflows are scanned for the site's own hostname: a www. literal,
   a named github.io host, or a .com/.net/.org hostname, so moving the site to
   a custom domain cannot quietly put that domain in the repository.
   A line carrying the EXEMPT_MARKER comment is exempt too - for a literal
   that is true of every installation (a pinned dependency's own vendor URL,
   not this one's), reviewed and left as plain, searchable text rather than
   split to dodge the scan. What this cannot catch is a person's name; that
   half is review's. */
/// <reference types="node" />
import { readdirSync, readFileSync } from 'node:fs'
import { join, relative } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

const ROOT = new URL('..', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1')
const WORKFLOWS = fileURLToPath(new URL('../../../../../.github/workflows/', import.meta.url))

const EXCLUDED_DIRS = new Set(['node_modules', 'dist'])

// A reviewed, genuinely-structural literal (true of every installation, not
// this one's) can carry this comment on its own line to opt out of the scan.
export const EXEMPT_MARKER = 'ethos:allow'

// Third-party sites the guide pages in content/ may name; a host must equal one
// of these or end in '.' + one of them.
export const THIRD_PARTY_DOMAINS = [
  'docker.com', // Docker install docs and download host
  'microsoft.com', // learn.microsoft.com WSL and Windows docs
  'git-scm.com', // Git install pages
  'brew.sh', // Homebrew and formulae.brew.sh
  'claude.com', // code.claude.com, the Claude Code docs
  'claude.ai', // the Claude Code installer, claude.ai/install.sh
  'github.com', // GitHub, cli.github.com, docs.github.com, users.noreply.github.com
  'githubusercontent.com', // the Homebrew installer's raw URL
  'containerd.io', // the apt/dnf package name, not a host
  'apple.com', // support.apple.com
  'curl.se', // the curl manual
  'example.com', // the sample commit address
  'docker.app', // the macOS bundle name Docker.app, matched as a bare token - not a host
  'chatgpt.com', // the Codex CLI installer and docs
  'x.ai', // the Grok CLI installer, docs and sign-in
  'ollama.com', // Ollama's own download page
]

const WWW_RULE = { name: 'a www. host', pattern: /\bwww\./i }
const IP_RULE = { name: 'a dotted-quad IP', pattern: /\b(?:\d{1,3}\.){3}\d{1,3}\b/ }

const RULES: { name: string; pattern: RegExp }[] = [
  { name: 'a URL', pattern: /https?:\/\/(?!localhost\b)\S+/ },
  IP_RULE,
  { name: 'an email address', pattern: /[\w.+-]+@[\w-]+\.[\w.-]+/ },
  { name: 'a public hostname', pattern: /\b[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.(?:com|net|org|io|dev|app|ai)\b(?!\.)/i },
  WWW_RULE,
]

const URL_HOST = /https?:\/\/([^\s/:?#)>"'`]+)/gi
const EMAIL_DOMAIN = /[\w.+-]+@([\w-]+(?:\.[\w-]+)+)/g
const BARE_HOST = /[\w<>-]+(?:\.[\w<>-]+)*\.(?:com|net|org|io|dev|app|ai)\b/gi

const WORKFLOW_RULES: { name: string; pattern: RegExp }[] = [
  WWW_RULE,
  { name: 'a github.io host', pattern: /[\w-]+\.github\.io\b/i },
  { name: 'a .com/.net/.org hostname', pattern: /\b[a-z0-9-]+(?:\.[a-z0-9-]+)*\.(?:com|net|org)\b/i },
]

type Scanned = { file: string; content: boolean }

function listFiles(dir: string, content = false): Scanned[] {
  const files: Scanned[] = []
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.isDirectory()) {
      if (EXCLUDED_DIRS.has(entry.name)) continue
      files.push(...listFiles(join(dir, entry.name), content || entry.name === 'content'))
    } else if (!entry.name.endsWith('.test.ts')) {
      files.push({ file: join(dir, entry.name), content })
    }
  }
  return files
}

function lineViolations(line: string): string[] {
  if (line.includes(EXEMPT_MARKER)) return []
  return RULES.filter(({ pattern }) => pattern.test(line)).map(({ name }) => name)
}

function isThirdParty(host: string): boolean {
  return THIRD_PARTY_DOMAINS.some((d) => host === d || host.endsWith('.' + d))
}

function contentLineViolations(line: string): string[] {
  if (line.includes(EXEMPT_MARKER)) return []
  const found = [IP_RULE, WWW_RULE].filter(({ pattern }) => pattern.test(line)).map(({ name }) => name)
  const hosts = new Set<string>()
  for (const m of line.matchAll(URL_HOST)) hosts.add(m[1].toLowerCase())
  for (const m of line.matchAll(EMAIL_DOMAIN)) hosts.add(m[1].toLowerCase())
  for (const m of line.matchAll(BARE_HOST)) hosts.add(m[0].toLowerCase())
  for (const host of hosts) {
    if (host === 'localhost' || host.startsWith('<') || isThirdParty(host)) continue
    found.push(`a host outside THIRD_PARTY_DOMAINS (${host}) - add it there if it is a third party`)
  }
  return found
}

function workflowViolations(line: string): string[] {
  if (line.includes(EXEMPT_MARKER)) return []
  return WORKFLOW_RULES.filter(({ pattern }) => pattern.test(line)).map(({ name }) => name)
}

export function violations(root: string): string[] {
  const found: string[] = []
  for (const { file, content } of listFiles(root)) {
    const lines = readFileSync(file, 'utf8').split('\n')
    for (let i = 0; i < lines.length; i++) {
      for (const name of content ? contentLineViolations(lines[i]) : lineViolations(lines[i])) {
        found.push(`${relative(root, file)}:${i + 1}: ${name}`)
      }
    }
  }
  return found
}

export function workflowFileViolations(dir: string): string[] {
  const found: string[] = []
  for (const entry of readdirSync(dir)) {
    if (!/\.ya?ml$/.test(entry)) continue
    const lines = readFileSync(join(dir, entry), 'utf8').split('\n')
    for (let i = 0; i < lines.length; i++) {
      for (const name of workflowViolations(lines[i])) found.push(`.github/workflows/${entry}:${i + 1}: ${name}`)
    }
  }
  return found
}

describe('ethos', () => {
  it('has nothing true of exactly one installation', () => {
    expect(violations(ROOT)).toEqual([])
  })

  it('has no site hostname in a workflow', () => {
    expect(workflowFileViolations(WORKFLOWS)).toEqual([])
  })
})

describe('lineViolations', () => {
  it.each([
    ['a URL', 'see https://example.com/path for more'],
    ['a dotted-quad IP', 'connect to 192.168.0.1 directly'],
    ['an email address', 'contact admin@example.com for help'],
    ['a public hostname', 'reach it at example.com today'],
    ['a www. host', 'host it at www.foo'],
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

describe('contentLineViolations', () => {
  it.each([
    ['a host outside', 'see https://notlisted.test-host.dev/x for more'],
    ['a host outside', 'published at https://someone.github.io/repo/'],
    ['a www. host', 'host it at www.foo'],
    ['a dotted-quad IP', 'connect to 192.168.0.1 directly'],
  ])('flags %s', (name: string, line: string) => {
    expect(contentLineViolations(line).some((v) => v.startsWith(name))).toBe(true)
  })

  it.each([
    'https://docs.docker.com/engine/install/',
    '`ID+USER@users.noreply.github.com`',
    'https://<owner>.github.io/<repo>/setup/',
    'http://<host>:8080',
    'http://localhost:3000',
    'https://curl.se/docs/manpage.html',
    'Docker.app',
    `https://notlisted.dev # ${EXEMPT_MARKER}`,
  ])('does not flag %s', (line: string) => {
    expect(contentLineViolations(line)).toEqual([])
  })

  it('reports each unlisted host once', () => {
    expect(contentLineViolations('https://a.test-host.dev and https://b.test-host.dev')).toHaveLength(2)
    expect(contentLineViolations('https://a.test-host.dev')).toHaveLength(1)
  })
})

describe('workflowViolations', () => {
  it.each([
    ['a www. host', '      - run: curl www.foo'],
    ['a github.io host', 'url: https://someone.github.io/repo'],
    ['a .com/.net/.org hostname', 'host: app.example.com'],
  ])('flags %s', (name: string, line: string) => {
    expect(workflowViolations(line)).toContain(name)
  })

  it.each(['REGISTRY: ghcr.io', 'registry: ghcr.io', 'run: make test-web'])('does not flag %s', (line: string) => {
    expect(workflowViolations(line)).toEqual([])
  })
})
