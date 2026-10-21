/* Which docs pages draw their diagrams, and what renderDocs left for them to
   draw, plus (HA-1244) which pages' scripts actually reach the mermaid chunk
   in the real build's chunk graph - not merely whether a page is fenced.

   A fenced page's own `<script>` src is not literally "docsDiagrams": the
   `dompurify` and `vite-preload` codeSplitting groups vite.config.ts carries
   mean a fenced page's head lists theme.ts's chunk, then each of those two
   groups' own chunks, then last the fenced page's own entry (docsDiagrams.ts
   bundled with mermaidDraw.ts, reached by no other page today) - an unfenced
   page lists only the first. So the script every page's drawing logic lands
   in, fenced or not, is always the last one on the page, and what
   distinguishes a fenced page's script from an unfenced one is its content,
   not its name - the `.hatch-docs-mermaid*` class names drawMermaid writes
   and reads, which survive minification as string literals. */
/// <reference types="node" />
import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { beforeAll, describe, expect, it } from 'vitest'
import { buildSite } from './testing/buildSite'
import { attr, tagsOf, withoutComments } from './testing/html'
import { reachable } from './lib/mermaidChunkGraph'

type Site = Awaited<ReturnType<typeof buildSite>>

// A page in the repository's own docs with a mermaid fence today
// (`apps/hatch/docs/the-lifecycle-of-a-ticket.md`) - not the only one,
// `deterministic-work-and-agent-work.md` carries one too, but the property
// below is driven by whether a page has a fence at all, not by which page it is.
const FENCED_SLUG = 'the-lifecycle-of-a-ticket'
const UNFENCED_SLUGS = ['getting-started']

let site: Site

beforeAll(async () => {
  site = await buildSite()
})

const pageScript = (html: string): string => {
  const scripts = tagsOf(html, 'script')
  const last = scripts[scripts.length - 1]
  return readFileSync(join(site.dir, attr(last, 'src')!.replace(/^\//, '')), 'utf8')
}

describe('a docs page with a fence', () => {
  let html: string
  beforeAll(() => {
    html = withoutComments(site.read(`docs/${FENCED_SLUG}/index.html`))
  })

  it('has a placeholder with the fence source as non-empty escaped text', () => {
    const containers = [...html.matchAll(/<div class="hatch-docs-mermaid">([\s\S]*?)<\/div>/g)]
    expect(containers.length).toBeGreaterThan(0)
    for (const [, inner] of containers) {
      const source = inner.match(/<pre class="hatch-docs-mermaid-source">([\s\S]*?)<\/pre>/)
      expect(source).not.toBeNull()
      expect(source![1].trim().length).toBeGreaterThan(0)
      expect(source![1]).not.toContain('<')
    }
  })

  it('carries its own script, after the theme script', () => {
    const scripts = tagsOf(html, 'script')
    expect(scripts.length).toBeGreaterThan(1)
    expect(attr(scripts[0], 'src')).toMatch(/theme/)
  })

  it("its script carries drawMermaid's own code", () => {
    const js = pageScript(html)
    expect(js).toContain('.hatch-docs-mermaid-source')
    expect(js).toContain('.hatch-docs-mermaid-error')
  })
})

describe.each(UNFENCED_SLUGS)('a docs page with no fence (%s)', (slug) => {
  it('names no placeholder, and its script carries none of drawMermaid\'s code', () => {
    const html = withoutComments(site.read(`docs/${slug}/index.html`))
    expect(html).not.toContain('hatch-docs-mermaid')
    expect(pageScript(html)).not.toContain('hatch-docs-mermaid')
  })
})

describe.each(['docs/index.html', 'index.html', '404.html'])('%s', (file) => {
  it('names no placeholder', () => {
    expect(withoutComments(site.read(file))).not.toContain('hatch-docs-mermaid')
  })
})

describe('the mermaid chunk graph', () => {
  const reachesMermaidChunk = (html: string): boolean => {
    const chunks = site.output.filter((chunk) => chunk.type === 'chunk')
    const scripts = tagsOf(html, 'script').map((tag) => attr(tag, 'src')!.replace(/^\//, ''))
    const found = reachable(chunks, scripts)
    return chunks.some((chunk) => found.has(chunk.fileName) && chunk.moduleIds.some((id) => id.endsWith('src/lib/mermaid.ts')))
  }

  it(`a fenced page (docs/${FENCED_SLUG}) reaches a chunk with src/lib/mermaid.ts among its modules`, () => {
    const html = withoutComments(site.read(`docs/${FENCED_SLUG}/index.html`))
    expect(reachesMermaidChunk(html)).toBe(true)
  })

  describe.each([`docs/${UNFENCED_SLUGS[0]}/index.html`, 'docs/index.html', 'index.html', '404.html'])('%s', (file) => {
    it('reaches no chunk with src/lib/mermaid.ts among its modules', () => {
      const html = withoutComments(site.read(file))
      expect(reachesMermaidChunk(html)).toBe(false)
    })
  })
})
