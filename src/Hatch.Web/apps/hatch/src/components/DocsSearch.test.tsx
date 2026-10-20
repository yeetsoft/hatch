import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { DocsSearch } from './DocsSearch';
import { docIndex, docSearchStatus, keepDocHighlight, resultId, searchDocs, type DocResult } from '../lib/docsSearch';

/** The address the reader is on - a string, or a location object when the test
    needs navigation state. There is no jsdom here, so nothing can type; the
    address is the way in (TopBar.test.tsx:5-11 has the reason). */
function render(entry: string | { pathname: string; search: string; state: unknown }): string {
  return renderToStaticMarkup(
    <MemoryRouter initialEntries={[entry]}>
      <DocsSearch />
    </MemoryRouter>,
  );
}

/** Where a row should go, built from the test's own pieces rather than the
    component's `docTarget`, and written the way renderToStaticMarkup writes an
    attribute (`&` as `&amp;`). */
function target(result: DocResult, term: string, extra = ''): string {
  const q = new URLSearchParams({ q: term }).toString();
  const search = extra ? `${q}&${extra}` : q;
  return `/docs/${result.slug}?${search}${result.anchor ? `#${result.anchor}` : ''}`.replace(/&/g, '&amp;');
}

/** A word straight out of the app's own index, not a hardcoded slug or
    snippet - DOC_PAGES changes as docs are added and rewritten. */
function term(): string {
  return docIndex()[0].title.split(/\s+/)[0];
}

/** Every <li role="option" ...>...</li> in document order - there is never a
    nested <li>, so a non-greedy match between the tags is exact. */
function options(html: string): { id: string; selected: boolean; body: string }[] {
  return [...html.matchAll(/<li\s+([^>]*)>([\s\S]*?)<\/li>/g)].map(([, attrs, body]) => ({
    id: /id="([^"]*)"/.exec(attrs)?.[1] ?? '',
    selected: /aria-selected="true"/.test(attrs),
    body,
  }));
}

describe('DocsSearch', () => {
  it('renders closed and empty: one labelled combobox, no list, no active descendant', () => {
    const html = render('/docs');

    expect([...html.matchAll(/aria-label="Search docs"/g)]).toHaveLength(1);
    expect(html).toMatch(/<input[^>]*role="combobox"/);
    expect(html).toMatch(/<input[^>]*aria-expanded="false"/);
    expect(html).not.toContain('role="listbox"');
    expect(html).not.toContain('aria-activedescendant');
  });

  it('draws at most ten rows, one highlighted and named by aria-activedescendant, each linking to its page and anchor', () => {
    const query = term();
    const { results, total } = searchDocs(query);
    expect(results.length).toBeGreaterThan(0);
    expect(results.length).toBeLessThanOrEqual(10);

    const html = render(`/docs?q=${encodeURIComponent(query)}`);
    const active = keepDocHighlight(results, null);

    expect(html).toMatch(/<input[^>]*role="combobox"/);
    expect(html).toMatch(/<input[^>]*aria-expanded="true"/);
    expect(html).toContain('role="listbox"');

    const rows = options(html);
    expect(rows).toHaveLength(results.length);

    const selected = rows.filter((r) => r.selected);
    expect(selected).toHaveLength(1);
    expect(selected[0].id).toBe(active);
    expect(html).toContain(`aria-activedescendant="${active}"`);

    let expectedMarkRuns = 0;
    results.forEach((result, index) => {
      const row = rows[index];
      expect(row.id).toBe(resultId(result));
      expect(row.selected).toBe(resultId(result) === active);
      expect(row.body).toContain(`href="${target(result, query)}"`);
      expect(row.body).toContain(result.title);
      if (result.heading !== null) expect(row.body).toContain(result.heading);
      expectedMarkRuns += result.marks.length;
    });

    expect([...html.matchAll(/<mark>/g)]).toHaveLength(expectedMarkRuns);

    const status = docSearchStatus(query, results.length, total);
    expect(html).toMatch(new RegExp(`aria-live="polite"[^>]*>${status.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}<`));
  });

  it('shows no list for a blank query', () => {
    const html = render('/docs?q=');
    expect(html).toMatch(/<input[^>]*aria-expanded="false"/);
    expect(html).not.toContain('role="listbox"');
  });

  it.each(['/docs/the-one-line', '/docs', '/docs/no-such-page'])(
    'reads ?q= on %s: the field holds it, the list is open on the first row',
    (path) => {
      const query = term();
      const { results } = searchDocs(query);
      const html = render(`${path}?q=${encodeURIComponent(query)}`);

      expect(html).toContain(`value="${query}"`);
      expect(html).toMatch(/<input[^>]*aria-expanded="true"/);
      expect(options(html).map((r) => r.id)).toEqual(results.map(resultId));
      expect(html).toContain(`aria-activedescendant="${resultId(results[0])}"`);
    },
  );

  it('reads a whitespace-only q as no query, the field showing it as typed', () => {
    const html = render('/docs?q=%20%20');

    expect(html).toContain('value="  "');
    expect(html).toMatch(/<input[^>]*aria-expanded="false"/);
    expect(html).not.toContain('role="listbox"');
    expect(html).not.toContain('aria-activedescendant');
  });

  it('keeps other params in every row target', () => {
    const query = term();
    const { results } = searchDocs(query);
    const rows = options(render(`/docs?q=${encodeURIComponent(query)}&flow=1`));

    expect(rows).toHaveLength(results.length);
    results.forEach((result, index) => {
      expect(rows[index].body).toContain(`href="${target(result, query, 'flow=1')}"`);
    });
    expect(target(results[0], query, 'flow=1')).toContain('&amp;flow=1');
  });

  it('carries a heading row anchor and q together: /docs/<slug>?q=<term>#<anchor>', () => {
    // The first word of some heading whose search lists an anchored row.
    let word = '';
    let anchored: DocResult | undefined;
    for (const entry of docIndex()) {
      if (entry.heading === null) continue;
      const candidate = entry.heading.split(/\s+/)[0];
      anchored = searchDocs(candidate).results.find((r) => r.anchor !== null);
      if (anchored) {
        word = candidate;
        break;
      }
    }
    expect(anchored).toBeDefined();

    const rows = options(render(`/docs?q=${encodeURIComponent(word)}`));
    const expected = `/docs/${anchored!.slug}?q=${new URLSearchParams({ q: word }).toString().slice(2)}#${anchored!.anchor}`;
    expect(rows.some((r) => r.body.includes(`href="${expected}"`))).toBe(true);
  });

  it('renders closed, text kept, for an entry whose navigation state says a result was taken', () => {
    const query = term();
    const slug = searchDocs(query).results[0].slug;
    const html = render({ pathname: `/docs/${slug}`, search: `?q=${encodeURIComponent(query)}`, state: { docsSearchTaken: true } });

    expect(html).toContain(`value="${query}"`);
    expect(html).toMatch(/<input[^>]*aria-expanded="false"/);
    expect(html).not.toContain('role="listbox"');
  });
});
