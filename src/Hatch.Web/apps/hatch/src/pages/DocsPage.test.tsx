import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { ThemeProvider } from '@hatch/ui';
import { CHANGELOG_LINK, DOC_PAGES, createSlugger, docNeighbours, docPage, docSections, renderDocs } from '../lib/docs';
import { DocsPage } from './DocsPage';

// The real sanitizer needs a DOM, which this workspace's tests do not have -
// see ProseClamp.test.tsx. The identity stub still shows what the page handed it.
vi.mock('dompurify', () => ({ default: { sanitize: (html: string) => html } }));
// clientLogger.ts reads `window` at import, and there is no jsdom here - the
// same stub PlaybookRow.test.tsx uses for the same reason.
vi.mock('../lib/clientLogger', () => ({ clientLogger: { error: () => {} } }));

const state = vi.hoisted(() => ({ phone: false }));
vi.mock('../lib/viewport', () => ({ usePhone: () => state.phone }));

function renderAt(path: string, phone = false): string {
  state.phone = phone;
  // DocsArticle reads useTheme() (HA-1058's drawing effect is keyed on the
  // resolved theme), which throws outside a <ThemeProvider> - themeStore.ts
  // guards every DOM read it makes for a window that is absent here, so the
  // provider itself renders fine with no jsdom.
  return renderToStaticMarkup(
    <ThemeProvider>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/docs" element={<DocsPage />} />
          <Route path="/docs/*" element={<DocsPage />} />
        </Routes>
      </MemoryRouter>
    </ThemeProvider>,
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

/** The `href="#…"` targets inside the outline's own nav - matched by its class
    directly, since the sidebar renders a <nav> of its own now too, and the
    outline's is a bare <nav> on the desk but a <nav> nested inside a
    <details> on a phone. */
function outlineHrefs(html: string): string[] {
  const nav = /<nav class="hatch-docs-outline"[^>]*>([\s\S]*?)<\/nav>/.exec(html);
  if (nav) return [...nav[1].matchAll(/href="#([^"]+)"/g)].map((m) => m[1]);
  const details = /<details class="hatch-docs-outline">([\s\S]*?)<\/details>/.exec(html);
  return [...(details?.[1] ?? '').matchAll(/href="#([^"]+)"/g)].map((m) => m[1]);
}

/** The `Previous`/`Next` targets inside the pager nav, by the link's own class -
    not by position, since one side is absent at either end of `DOC_PAGES`. */
function pagerHrefs(html: string): { previous: string | null; next: string | null } {
  const side = (which: 'previous' | 'next'): string | null => {
    const tag = new RegExp(`<a[^>]*class="[^"]*hatch-docs-pager-${which}[^"]*"[^>]*>`).exec(html);
    if (!tag) return null;
    return /href="([^"]+)"/.exec(tag[0])?.[1] ?? null;
  };
  return { previous: side('previous'), next: side('next') };
}

describe('DocsPage', () => {
  describe('the landing', () => {
    const html = renderAt('/docs');

    it('titles the page Docs, once', () => {
      expect(html).toContain('<h1>Docs</h1>');
      expect(html.split('<h1').length - 1).toBe(1);
    });

    it('has no pager', () => {
      expect(html).not.toContain('hatch-docs-pager');
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

    it('leads Reference with Changelog, ahead of its first real page', () => {
      const sections = docSections(DOC_PAGES);
      const reference = sections.find((s) => s.section === 'Reference');
      expect(reference).toBeDefined();

      const changelog = html.indexOf(`href="${CHANGELOG_LINK.to}"`);
      const firstPage = html.indexOf(`href="/docs/${reference!.pages[0].slug}"`);
      expect(changelog).toBeGreaterThan(-1);
      expect(changelog).toBeLessThan(firstPage);
      expect(html).toContain(`>${CHANGELOG_LINK.title}<`);
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

    it('renders the same h1, outline and article with a ?q= as without', () => {
      const page = DOC_PAGES[0];
      const term = page.title.split(/\s+/)[0];
      const outline = renderDocs(page.body).outline;
      const article = (html: string) => /<article class="hatch-docs-body"[\s\S]*?<\/article>/.exec(html)?.[0];

      const plain = renderAt(`/docs/${page.slug}`);
      const searched = renderAt(`/docs/${page.slug}?q=${encodeURIComponent(term)}`);

      expect(searched).toContain(`<h1>${escapeText(page.title)}</h1>`);
      expect(searched.split('<h1').length - 1).toBe(1);
      expect(outlineHrefs(searched)).toEqual(outline.map((entry) => entry.id));
      expect(outlineHrefs(searched)).toEqual(outlineHrefs(plain));
      expect(article(plain)).toBeDefined();
      expect(article(searched)).toBe(article(plain));
    });

    it('orders the sidebar, then the outline, then the article, then the pager in markup - CSS places them on the desk', () => {
      const page = DOC_PAGES[0];
      const html = renderAt(`/docs/${page.slug}`);

      const sidebar = html.indexOf('class="hatch-docs-sidebar"');
      const outline = html.indexOf('class="hatch-docs-outline"');
      const article = html.indexOf('class="hatch-docs-body"');
      const pager = html.indexOf('class="hatch-docs-pager"');

      expect(sidebar).toBeGreaterThanOrEqual(0);
      expect(sidebar).toBeLessThan(outline);
      expect(outline).toBeLessThan(article);
      expect(article).toBeLessThan(pager);
    });

    it('has a Next to the second page and no Previous, at the first page', () => {
      const html = renderAt(`/docs/${DOC_PAGES[0].slug}`);
      const { previous, next } = pagerHrefs(html);

      expect(previous).toBeNull();
      expect(next).toBe(`/docs/${DOC_PAGES[1].slug}`);
    });

    it('has a Previous to the second-to-last page and no Next, at the last page', () => {
      const last = DOC_PAGES[DOC_PAGES.length - 1];
      const secondToLast = DOC_PAGES[DOC_PAGES.length - 2];
      const html = renderAt(`/docs/${last.slug}`);
      const { previous, next } = pagerHrefs(html);

      expect(previous).toBe(`/docs/${secondToLast.slug}`);
      expect(next).toBeNull();
    });

    it("crosses into the next section's first page, at a section's last page", () => {
      const sections = docSections(DOC_PAGES);
      const lastOfFirstSection = sections[0].pages[sections[0].pages.length - 1];
      const firstOfSecondSection = sections[1].pages[0];
      const html = renderAt(`/docs/${lastOfFirstSection.slug}`);

      expect(pagerHrefs(html).next).toBe(`/docs/${firstOfSecondSection.slug}`);
    });

    it('matches docNeighbours for every page', () => {
      for (const page of DOC_PAGES) {
        const { previous, next } = docNeighbours(DOC_PAGES, page.slug);
        const html = renderAt(`/docs/${page.slug}`);

        expect(pagerHrefs(html)).toEqual({
          previous: previous ? `/docs/${previous.slug}` : null,
          next: next ? `/docs/${next.slug}` : null,
        });
      }
    });

    it("outlines the page's ## headings and not the ones inside fenced code", () => {
      const page = DOC_PAGES[0];
      const outline = renderDocs(page.body).outline;
      const ids = outline.filter((entry) => entry.depth === 2).map((entry) => entry.id);

      expect(ids).toEqual(h2Ids(page.body));
    });
  });

  it('outlines One increment and the loop with a heading for one increment of hatch work', () => {
    const page = docPage(DOC_PAGES, 'one-increment-and-the-loop');
    expect(page).toBeDefined();
    const html = renderAt('/docs/one-increment-and-the-loop');

    expect(outlineHrefs(html)).toContain('one-increment-hatch-work');
  });

  it('draws How work flows with a mermaid placeholder for each diagram', () => {
    const page = docPage(DOC_PAGES, 'how-work-flows');
    expect(page).toBeDefined();
    const html = renderAt('/docs/how-work-flows');

    expect(html.split('class="hatch-docs-mermaid"').length - 1).toBe(2);
  });

  describe('anything else', () => {
    for (const path of ['/docs/no-such-page', '/docs/a/b']) {
      it(`shows a not-found state inside the docs frame for ${path}`, () => {
        const html = renderAt(path);

        expect(html).toContain('class="hatch-docs"');
        expect(html).toContain('href="/docs"');
        expect(html).not.toContain('hatch-docs-body');
        expect(html).not.toContain('hatch-docs-pager');
      });

      it(`sits the sidebar beside the not-found message for ${path}`, () => {
        const html = renderAt(path);

        expect(html).toContain('class="hatch-docs-sidebar"');
      });
    }
  });

  // A phone renders three <nav>s now - the sidebar's own, the outline's, and the
  // pager's - where the desk's sidebar, outline and pager are the only three to
  // begin with.
  it('folds the outline into a closed details on a phone, still three navs', () => {
    const page = DOC_PAGES[0];
    const html = renderAt(`/docs/${page.slug}`, true);
    const outline = renderDocs(page.body).outline;

    expect(html).toContain('<details class="hatch-docs-outline">');
    expect(html).toContain('<details class="hatch-docs-sidebar-phone">');
    expect(outlineHrefs(html)).toEqual(outline.map((entry) => entry.id));
    expect(html.split('<nav').length - 1).toBe(3);
  });
});
