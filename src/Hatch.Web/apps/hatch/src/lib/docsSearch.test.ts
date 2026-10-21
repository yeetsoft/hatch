/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import type { DocPage } from './docs';
import { docIndex } from './docsIndex';
import {
  docTarget,
  DOC_SEARCH_LIMIT,
  docSearchStatus,
  foldText,
  keepDocHighlight,
  moveDocHighlight,
  resultId,
  searchDocs,
  withDocQuery,
  type DocResult,
} from './docsSearch';

function page(body: string, overrides: Partial<DocPage> = {}): DocPage {
  return { slug: 'x', title: 'X', section: 'Reference', order: 1, summary: 'A page.', ...overrides, body };
}

describe('docsSearch.ts imports', () => {
  const text = readFileSync(new URL('./docsSearch.ts', import.meta.url), 'utf8')
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/\/\/.*$/gm, '');

  it('names only ./docsIndex and ./goTo, both by type, so a page with no markdown can import it', () => {
    const specifiers = [
      ...[...text.matchAll(/\bfrom\s+'([^']+)'/g)].map((m) => m[1]),
      ...[...text.matchAll(/\bimport\(\s*'([^']+)'\s*\)/g)].map((m) => m[1]),
      ...[...text.matchAll(/^\s*import\s+'([^']+)'/gm)].map((m) => m[1]),
    ];
    expect([...new Set(specifiers)].sort()).toEqual(['./docsIndex', './goTo']);

    const statics = [...text.matchAll(/\bimport\b[^;]*?\bfrom\s+'(?:\.\/docsIndex|\.\/goTo)'/g)].map((m) => m[0]);
    expect(statics).toHaveLength(2);
    for (const statement of statics) expect(statement).toMatch(/^import\s+type\b/);
  });
});

describe('foldText', () => {
  it('preserves length on an accented string', () => {
    const text = 'déjà vu, café';
    expect(foldText(text).length).toBe(text.length);
  });

  it('finds "deja" in "déjà" once folded', () => {
    expect(foldText('déjà')).toContain('deja');
  });

  it('ignores case both ways', () => {
    expect(foldText('DÉJÀ')).toBe(foldText('déjà'));
    expect(foldText('Hello')).toBe('hello');
  });
});

describe('searchDocs: ranking', () => {
  it('ranks a title match above a heading match above a body match', () => {
    const titlePage = page('Nothing special here.\n', { slug: 'title-page', title: 'Zephyr Guide' });
    const headingPage = page('## Zephyr Notes\n\nSection body.\n', { slug: 'heading-page' });
    const bodyPage = page('Some assorted prose mentions zephyr once.\n', { slug: 'body-page' });

    const entries = docIndex([titlePage, headingPage, bodyPage]);
    const { results } = searchDocs('zephyr', entries);

    expect(results.map((r) => ({ slug: r.slug, where: r.where }))).toEqual([
      { slug: 'title-page', where: 'title' },
      { slug: 'heading-page', where: 'heading' },
      { slug: 'body-page', where: 'body' },
    ]);
  });

  it('keeps two body matches in entry order', () => {
    const first = page('A gadget is mentioned here.\n', { slug: 'first' });
    const second = page('Another gadget mention, later.\n', { slug: 'second' });

    const { results } = searchDocs('gadget', docIndex([first, second]));
    expect(results.map((r) => r.slug)).toEqual(['first', 'second']);
  });

  it('gives a page matched on its title exactly one result, not one per section', () => {
    const doc = page('## Orbit One\n\nTalk of orbit here.\n\n## Orbit Two\n\nMore orbit here.\n', {
      slug: 'orbit',
      title: 'Orbit Guide',
    });

    const { results } = searchDocs('orbit', docIndex([doc]));
    expect(results).toHaveLength(1);
    expect(results[0]).toMatchObject({ slug: 'orbit', where: 'title', heading: null, anchor: null });
  });
});

describe('searchDocs: partial words', () => {
  it('matches a query ending mid-word', () => {
    const doc = page('Read about go-to-work before you start.\n');
    const { results } = searchDocs('go-to-w', docIndex([doc]));
    expect(results).toHaveLength(1);
  });

  it('matches only entries holding every term, in any order', () => {
    const both = page('This page talks about alpha and beta together.\n', { slug: 'both' });
    const onlyAlpha = page('This page only mentions alpha.\n', { slug: 'only-alpha' });

    const entries = docIndex([both, onlyAlpha]);
    expect(searchDocs('alpha beta', entries).results.map((r) => r.slug)).toEqual(['both']);
    expect(searchDocs('beta alpha', entries).results.map((r) => r.slug)).toEqual(['both']);
  });
});

describe('searchDocs: empty query', () => {
  it('gives no results for an empty string', () => {
    expect(searchDocs('', docIndex([page('alpha beta')]))).toEqual({ results: [], total: 0 });
  });

  it('gives no results for a whitespace-only string', () => {
    expect(searchDocs('   ', docIndex([page('alpha beta')]))).toEqual({ results: [], total: 0 });
  });
});

describe('searchDocs: the cap', () => {
  it('caps at DOC_SEARCH_LIMIT and reports the true total', () => {
    const pages = Array.from({ length: 15 }, (_, i) => page(`Page number ${i} mentions widget here.\n`, { slug: `p${i}` }));
    const { results, total } = searchDocs('widget', docIndex(pages));
    expect(results).toHaveLength(DOC_SEARCH_LIMIT);
    expect(total).toBe(15);
  });
});

describe('searchDocs: marks', () => {
  it('merges overlapping term matches into one run', () => {
    const doc = page('xx abcdef yy\n');
    const { results } = searchDocs('abc bcd', docIndex([doc]));
    expect(results).toHaveLength(1);
    expect(results[0].marks).toHaveLength(1);

    const [mark] = results[0].marks;
    expect(foldText(results[0].snippet.slice(mark.start, mark.end))).toBe('abcd');
  });

  it('gives marks that index into the snippet and fold equal to the term', () => {
    const doc = page('An Otter swims by the river.\n');
    const { results } = searchDocs('otter', docIndex([doc]));
    expect(results).toHaveLength(1);
    expect(results[0].marks).toHaveLength(1);

    const [mark] = results[0].marks;
    expect(foldText(results[0].snippet.slice(mark.start, mark.end))).toBe('otter');
  });
});

describe('searchDocs: status line', () => {
  it('is blank for a blank query', () => {
    expect(docSearchStatus('', 0, 0)).toBe('');
    expect(docSearchStatus('   ', 0, 0)).toBe('');
  });

  it('says no page matches when nothing does', () => {
    expect(docSearchStatus('nope', 0, 0)).toBe('no page matches');
  });

  it('says how many are shown while more are hidden', () => {
    expect(docSearchStatus('a', 10, 23)).toBe('10 of 23 — keep typing');
  });

  it('counts a single page and several results differently', () => {
    expect(docSearchStatus('a', 1, 1)).toBe('1 page');
    expect(docSearchStatus('a', 4, 4)).toBe('4 results');
  });
});

describe('docsSearch: keyboard helpers', () => {
  function result(slug: string, anchor: string | null): DocResult {
    return { slug, title: 'T', heading: null, anchor, where: 'body', snippet: '', marks: [] };
  }

  const results = [result('a', null), result('b', 'one'), result('c', 'two')];

  describe('keepDocHighlight', () => {
    it('keeps a listed id', () => {
      expect(keepDocHighlight(results, resultId(results[1]))).toBe(resultId(results[1]));
    });

    it('replaces an unlisted id with the first result', () => {
      expect(keepDocHighlight(results, 'nope#nope')).toBe(resultId(results[0]));
    });

    it('answers null for no results', () => {
      expect(keepDocHighlight([], 'anything')).toBeNull();
      expect(keepDocHighlight([], null)).toBeNull();
    });
  });

  describe('moveDocHighlight', () => {
    it('moves forward and backward within the list', () => {
      expect(moveDocHighlight(results, resultId(results[0]), 1)).toBe(resultId(results[1]));
      expect(moveDocHighlight(results, resultId(results[2]), -1)).toBe(resultId(results[1]));
    });

    it('clamps at the last row rather than wrapping', () => {
      expect(moveDocHighlight(results, resultId(results[2]), 1)).toBe(resultId(results[2]));
    });

    it('clamps at the first row rather than wrapping', () => {
      expect(moveDocHighlight(results, resultId(results[0]), -1)).toBe(resultId(results[0]));
    });
  });
});

describe('withDocQuery', () => {
  it('sets q on params that have none', () => {
    expect(withDocQuery(new URLSearchParams(), 'go').get('q')).toBe('go');
  });

  it('replaces an existing q', () => {
    expect(withDocQuery(new URLSearchParams('q=old'), 'new').getAll('q')).toEqual(['new']);
  });

  it('removes q for an empty query, leaving no "q=" behind', () => {
    const out = withDocQuery(new URLSearchParams('flow=1&q=old'), '');
    expect(out.has('q')).toBe(false);
    expect(out.toString()).toBe('flow=1');
    expect(withDocQuery(new URLSearchParams('q=old'), '').toString()).toBe('');
  });

  it('keeps every other param and their order', () => {
    expect(withDocQuery(new URLSearchParams('a=1&q=old&b=2'), 'new').toString()).toBe('a=1&q=new&b=2');
    expect(withDocQuery(new URLSearchParams('a=1&b=2'), 'new').toString()).toBe('a=1&b=2&q=new');
  });

  it('does not mutate its input', () => {
    const input = new URLSearchParams('a=1&q=old');
    withDocQuery(input, 'new');
    withDocQuery(input, '');
    expect(input.toString()).toBe('a=1&q=old');
  });

  it('keeps whitespace as typed rather than trimming it', () => {
    expect(withDocQuery(new URLSearchParams(), '  ').get('q')).toBe('  ');
  });

  it('round-trips a value holding &, #, a space and %', () => {
    const q = 'a&b #c %20 d';
    const out = withDocQuery(new URLSearchParams('flow=1'), q);
    const back = new URLSearchParams(out.toString());
    expect(back.get('q')).toBe(q);
    expect(back.get('flow')).toBe('1');
    expect(out.toString()).not.toContain('#');
  });
});

describe('docTarget', () => {
  const withAnchor: DocResult = { slug: 'page', title: 'P', heading: 'H', anchor: 'h', where: 'heading', snippet: '', marks: [] };
  const bare: DocResult = { ...withAnchor, heading: null, anchor: null, where: 'title' };

  it('puts the query between the page and its anchor', () => {
    expect(docTarget(withAnchor, new URLSearchParams(), 'go')).toBe('/docs/page?q=go#h');
  });

  it('keeps other params and writes no "?" when there is nothing to carry', () => {
    expect(docTarget(bare, new URLSearchParams('flow=1'), 'go')).toBe('/docs/page?flow=1&q=go');
    expect(docTarget(bare, new URLSearchParams(), '')).toBe('/docs/page');
  });

  it('cannot let a query end the search or corrupt the fragment', () => {
    const target = docTarget(withAnchor, new URLSearchParams(), 'a#b&c');
    expect(target.split('#')).toHaveLength(2);
    expect(target.endsWith('#h')).toBe(true);
  });

  it('addresses the page through a custom resolver instead of /docs/<slug>', () => {
    const page = (slug: string) => `/site/${slug}/`;
    expect(docTarget(withAnchor, new URLSearchParams(), 'go', page)).toBe('/site/page/?q=go#h');
    expect(docTarget(bare, new URLSearchParams(), '', page)).toBe('/site/page/');
  });
});
