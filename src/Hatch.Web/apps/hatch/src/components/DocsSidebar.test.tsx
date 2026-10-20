import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { CHANGELOG_LINK, DOC_PAGES, docSections } from '../lib/docs';
import { DocsSidebar } from './DocsSidebar';

const state = vi.hoisted(() => ({ phone: false }));
vi.mock('../lib/viewport', () => ({ usePhone: () => state.phone }));

function renderAt(path: string, phone = false): string {
  state.phone = phone;
  return renderToStaticMarkup(
    <MemoryRouter initialEntries={[path]}>
      <DocsSidebar />
    </MemoryRouter>,
  );
}

/** Every <a ...> tag's href and whether it carries aria-current="page", in
    document order - attribute order inside the tag is not guaranteed. */
function anchors(html: string): { href: string; current: boolean }[] {
  return [...html.matchAll(/<a\s+([^>]*)>/g)].map(([, attrs]) => ({
    href: /href="([^"]*)"/.exec(attrs)?.[1] ?? '',
    current: /aria-current="page"/.test(attrs),
  }));
}

describe('DocsSidebar', () => {
  const sections = docSections(DOC_PAGES);
  const current = DOC_PAGES[0];

  it('summarises every section, in docSections order', () => {
    const html = renderAt(`/docs/${current.slug}`);
    const summaries = [...html.matchAll(/<summary>(.*?)<\/summary>/g)].map((m) => m[1]);
    expect(summaries).toEqual(sections.map((s) => s.section));
  });

  it('links every page to its slug, in docSections order', () => {
    const html = renderAt(`/docs/${current.slug}`);
    const hrefs = anchors(html)
      .map((a) => a.href)
      .filter((href) => href.startsWith('/docs/'));
    expect(hrefs).toEqual(sections.flatMap((s) => s.pages.map((p) => `/docs/${p.slug}`)));
  });

  it('opens every section', () => {
    const html = renderAt(`/docs/${current.slug}`);
    expect([...html.matchAll(/<details[^>]*\sopen/g)].length).toBe(sections.length);
  });

  it('marks exactly the current slug current', () => {
    const html = renderAt(`/docs/${current.slug}`);
    const marked = anchors(html).filter((a) => a.current);
    expect(marked.length).toBe(1);
    expect(marked[0].href).toBe(`/docs/${current.slug}`);
  });

  it('marks the landing link current at /docs', () => {
    const html = renderAt('/docs');
    const marked = anchors(html).filter((a) => a.current);
    expect(marked.length).toBe(1);
    expect(marked[0].href).toBe('/docs');
  });

  it('wraps in one outer closed details on a phone, sections still open', () => {
    const html = renderAt(`/docs/${current.slug}`, true);

    expect(html).toContain('<details class="hatch-docs-sidebar-phone">');
    expect([...html.matchAll(/<details[^>]*\sopen/g)].length).toBe(sections.length);
  });

  it('leads Reference with Changelog, ahead of its first real page', () => {
    const html = renderAt(`/docs/${current.slug}`);
    const reference = sections.find((s) => s.section === 'Reference');
    expect(reference).toBeDefined();

    const changelog = html.indexOf(`href="${CHANGELOG_LINK.to}"`);
    const firstPage = html.indexOf(`href="/docs/${reference!.pages[0].slug}"`);
    expect(changelog).toBeGreaterThan(-1);
    expect(changelog).toBeLessThan(firstPage);
    expect(html).toContain(`>${CHANGELOG_LINK.title}<`);
  });

  it('puts the search field ahead of the Docs home link, on the desk and inside the phone fold', () => {
    const desk = renderAt(`/docs/${current.slug}`);
    expect(desk.indexOf('role="combobox"')).toBeGreaterThan(-1);
    expect(desk.indexOf('role="combobox"')).toBeLessThan(desk.indexOf('href="/docs"'));

    const phone = renderAt(`/docs/${current.slug}`, true);
    expect(phone.indexOf('role="combobox"')).toBeGreaterThan(-1);
    expect(phone.indexOf('role="combobox"')).toBeLessThan(phone.indexOf('href="/docs"'));
  });
});
