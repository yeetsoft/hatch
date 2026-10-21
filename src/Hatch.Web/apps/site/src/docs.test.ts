/* One docs/<slug>/index.html per markdown file under apps/hatch/docs/, built
   by vite-plugin-docs.ts through the app's own docs module. Regex over the
   built text, the same as pages.test.ts: there is no DOM in this runner. */
/// <reference types="node" />
import { readFileSync, readdirSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { beforeAll, describe, expect, it } from 'vitest'
import { escapeHtml } from '../vite-plugin-site'
import { buildSite } from './testing/buildSite'
import { tagsOf, withoutComments } from './testing/html'

type Site = Awaited<ReturnType<typeof buildSite>>

// Read with readdirSync rather than through DOC_PAGES, so this test cannot
// agree with a bug in the plugin that also miscounts DOC_PAGES.
const DOCS_DIR = join(dirname(fileURLToPath(import.meta.url)), '../../hatch/docs')

function frontmatterTitle(raw: string): string {
  const lines = raw.split(/\r?\n/)
  const close = lines.indexOf('---', 1)
  const line = lines.slice(1, close).find((l) => l.startsWith('title:'))
  if (!line) throw new Error('fixture has no title: line')
  return line.slice('title:'.length).trim()
}

let site: Site
let slugs: string[]

beforeAll(async () => {
  site = await buildSite()
  slugs = readdirSync(DOCS_DIR)
    .filter((f) => f.endsWith('.md'))
    .map((f) => f.slice(0, -'.md'.length))
})

describe('docs pages', () => {
  it('builds one docs/<slug>/index.html per markdown file', () => {
    expect(slugs.length).toBeGreaterThan(0)
    for (const slug of slugs) expect(() => site.read(`docs/${slug}/index.html`)).not.toThrow()
  })

  it("has the page's frontmatter title as its only <h1>", () => {
    for (const slug of slugs) {
      const title = frontmatterTitle(readFileSync(join(DOCS_DIR, `${slug}.md`), 'utf8'))
      const html = withoutComments(site.read(`docs/${slug}/index.html`))
      expect(tagsOf(html, 'h1'), slug).toHaveLength(1)
      expect(html, slug).toContain(`<h1>${escapeHtml(title)}</h1>`)
    }
  })

  it('carries the heading id the app gives getting-started\'s "Prerequisites" section', () => {
    const html = site.read('docs/getting-started/index.html')
    expect(html).toContain('id="prerequisites"')
  })

  it('links getting-started to backup-restore-and-replication as the site address', () => {
    const html = site.read('docs/getting-started/index.html')
    expect(html).toContain('href="/docs/backup-restore-and-replication/#restoring"')
  })

  it('links getting-started to backup-restore-and-replication under a base path', async () => {
    const projectSite = await buildSite({ base_path: 'project' })
    const html = projectSite.read('docs/getting-started/index.html')
    expect(html).toContain('href="/project/docs/backup-restore-and-replication/#restoring"')
  })
})
