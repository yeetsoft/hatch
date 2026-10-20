import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { DOC_PAGES, createSlugger, docPage, docSections, renderDocs } from '../lib/docs';
import { DocsPage } from './DocsPage';

// The real sanitizer needs a DOM, which this workspace's tests do not have -
// see ProseClamp.test.tsx. The identity stub still shows what the page handed it.
vi.mock('dompurify', () => ({ default: { sanitize: (html: string) => html } }));

const state = vi.hoisted(() => ({ phone: false }));
vi.mock('../lib/viewport', () => ({ usePhone: () => state.phone }));

function renderAt(path: string, phone = false): string {
  state.phone = phone;
  return renderToStaticMarkup(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/docs" element={<DocsPage />} />
        <Route path="/docs/*" element={<DocsPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

/** How React escapes text content, so a string compared against rendered HTML
    matches even when it carries an apostrophe or other entity-worthy character. */
function escapeText(s: string): string {
  return s
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#x27;');
}

/** The ids of the document's `## ` lines, skipping fenced code. */
function h2Ids(markdown: string): string[] {
  const slug = createSlugger();
  const ids: string[] = [];
  let fence = 0;
  for (const line of markdown.split(/\r?\n/)) {
    const run = line.match(/^ {0,3}(`{3,})/);
    if (fence) {
      if (run && run[1].length >= fence && /^ {0,3}`+\s*$/.test(line)) fence = 0;
      continue;
    }
    if (run) {
      fence = run[1].length;
      continue;
    }
    const heading = line.match(/^## (.+?)\s*#*\s*$/);
    if (heading) ids.push(slug(heading[1]));
  }
  return ids;
}

/** The `href="#…"` targets inside the first <nav>…</nav>, or the <details>. */
function outlineHrefs(html: string): string[] {
  const nav = html.slice(html.indexOf('<nav'), html.indexOf('</nav>'));
  return [...nav.matchAll(/href="#([^"]+)"/g)].map((m) => m[1]);
}

describe('DocsPage', () => {
  describe('the landing', () => {
    const html = renderAt('/docs');

    it('titles the page Docs, once', () => {
      expect(html).toContain('<h1>Docs</h1>');
      expect(html.split('<h1').length - 1).toBe(1);
    });

    it('lists every section, in docSections order', () => {
      const sections = docSections(DOC_PAGES);
      const headings = [...html.matchAll(/<h2[^>]*>(.*?)<\/h2>/g)].map((m) => m[1]);
      expect(headings).toEqual(sections.map((s) => escapeText(s.section)));
    });

    it('links every page to its slug, with its summary beside it', () => {
      for (const page of DOC_PAGES) {
        expect(html).toContain(`href="/docs/${page.slug}"`);
        expect(html).toContain(escapeText(page.title));
        expect(html).toContain(escapeText(page.summary));
      }
    });
  });

  describe('a page', () => {
    const cases = [DOC_PAGES[0], DOC_PAGES[DOC_PAGES.length - 1]];

    for (const page of cases) {
      it(`draws ${page.slug} under its own title and outline`, () => {
        const html = renderAt(`/docs/${page.slug}`);
        const outline = renderDocs(page.body).outline;

        expect(html).toContain(`<h1>${escapeText(page.title)}</h1>`);
        expect(html.split('<h1').length - 1).toBe(1);
        expect(outlineHrefs(html)).toEqual(outline.map((entry) => entry.id));
        expect(html).toContain('<article class="hatch-docs-body"');
      });
    }

    it("outlines the page's ## headings and not the ones inside fenced code", () => {
      const page = DOC_PAGES[0];
      const outline = renderDocs(page.body).outline;
      const ids = outline.filter((entry) => entry.depth === 2).map((entry) => entry.id);

      expect(ids).toEqual(h2Ids(page.body));
    });
  });

  it('outlines the CLI page with a heading for one increment of hatch work', () => {
    const page = docPage(DOC_PAGES, 'the-hatch-cli-command-by-command');
    expect(page).toBeDefined();
    const html = renderAt('/docs/the-hatch-cli-command-by-command');

    expect(outlineHrefs(html)).toContain('one-increment-hatch-work');
  });

  describe('anything else', () => {
    for (const path of ['/docs/no-such-page', '/docs/a/b']) {
      it(`shows a not-found state inside the docs frame for ${path}`, () => {
        const html = renderAt(path);

        expect(html).toContain('class="hatch-docs"');
        expect(html).toContain('href="/docs"');
        expect(html).not.toContain('hatch-docs-body');
      });
    }
  });

  it('folds the outline into a closed details on a phone, still one nav', () => {
    const page = DOC_PAGES[0];
    const html = renderAt(`/docs/${page.slug}`, true);
    const outline = renderDocs(page.body).outline;

    expect(html).toContain('<details');
    expect(html).not.toMatch(/<details[^>]*\sopen/);
    expect(outlineHrefs(html)).toEqual(outline.map((entry) => entry.id));
    expect(html.split('<nav').length - 1).toBe(1);
  });
});
