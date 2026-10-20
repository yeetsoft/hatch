import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { DocsSearch } from './DocsSearch';
import { docIndex, docSearchStatus, keepDocHighlight, resultId, searchDocs, type DocResult } from '../lib/docsSearch';

function render(initialQuery?: string): string {
  return renderToStaticMarkup(
    <MemoryRouter>
      <DocsSearch initialQuery={initialQuery} />
    </MemoryRouter>,
  );
}

function docPath(result: DocResult): string {
  return result.anchor ? `/docs/${result.slug}#${result.anchor}` : `/docs/${result.slug}`;
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
    const html = render();

    expect([...html.matchAll(/aria-label="Search docs"/g)]).toHaveLength(1);
    expect(html).toMatch(/<input[^>]*role="combobox"/);
    expect(html).toMatch(/<input[^>]*aria-expanded="false"/);
    expect(html).not.toContain('role="listbox"');
    expect(html).not.toContain('aria-activedescendant');
  });

  it('draws at most ten rows, one highlighted and named by aria-activedescendant, each linking to its page and anchor', () => {
    // A word straight out of the app's own index, not a hardcoded slug or
    // snippet - DOC_PAGES changes as docs are added and rewritten.
    const entries = docIndex();
    const query = entries[0].title.split(/\s+/)[0];
    const { results, total } = searchDocs(query);
    expect(results.length).toBeGreaterThan(0);
    expect(results.length).toBeLessThanOrEqual(10);

    const html = render(query);
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
      expect(row.body).toContain(`href="${docPath(result)}"`);
      expect(row.body).toContain(result.title);
      if (result.heading !== null) expect(row.body).toContain(result.heading);
      expectedMarkRuns += result.marks.length;
    });

    expect([...html.matchAll(/<mark>/g)]).toHaveLength(expectedMarkRuns);

    const status = docSearchStatus(query, results.length, total);
    expect(html).toMatch(new RegExp(`aria-live="polite"[^>]*>${status.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}<`));
  });

  it('shows no list for a blank query', () => {
    const html = render('');
    expect(html).toMatch(/<input[^>]*aria-expanded="false"/);
    expect(html).not.toContain('role="listbox"');
  });
});
