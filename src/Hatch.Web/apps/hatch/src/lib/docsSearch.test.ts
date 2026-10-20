import { describe, expect, it } from 'vitest';
import { DOC_PAGES, renderDocs, type DocPage } from './docs';
import {
  docIndex,
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

describe('docIndex: splitting a page into entries', () => {
  it('gives a page with no heading exactly one entry, carrying its prose', () => {
    const entries = docIndex([page('Just a paragraph of prose.\n')]);
    expect(entries).toHaveLength(1);
    expect(entries[0]).toMatchObject({ heading: null, anchor: null, text: 'Just a paragraph of prose.' });
  });

  it('gives a page with two headings three entries, the first with no heading or anchor', () => {
    const entries = docIndex([page('Opening run.\n\n## First\n\nFirst body.\n\n## Second\n\nSecond body.\n')]);
    expect(entries).toHaveLength(3);
    expect(entries[0]).toMatchObject({ heading: null, anchor: null, text: 'Opening run.' });
    expect(entries[1]).toMatchObject({ heading: 'First', anchor: 'first', text: 'First First body.' });
    expect(entries[2]).toMatchObject({ heading: 'Second', anchor: 'second', text: 'Second Second body.' });
  });

  it('copies title and summary onto every entry', () => {
    const entries = docIndex([page('Opening.\n\n## A\n\nBody.\n', { title: 'Title here', summary: 'Summary here.' })]);
    for (const e of entries) expect(e).toMatchObject({ title: 'Title here', summary: 'Summary here.' });
  });

  it('starts no entry at a "##" line inside a fenced block', () => {
    const body = 'Prose.\n\n````markdown\n## Not a heading\n````\n';
    const entries = docIndex([page(body)]);
    expect(entries).toHaveLength(1);
    expect(entries[0].heading).toBeNull();
  });

  it('leaves a mermaid fence out of the index but keeps a bash fence in', () => {
    const body = '```mermaid\ngraph TD; A-->B;\n```\n\n```bash\necho hello\n```\n';
    const entries = docIndex([page(body)]);
    expect(entries).toHaveLength(1);
    expect(entries[0].text).not.toContain('graph TD');
    expect(entries[0].text).toContain('echo hello');
  });

  it('strips markup: code spans, link targets and emphasis', () => {
    const body = 'See `code` and [a link](https://example.invalid/path) and *em* and **strong**.\n';
    const entries = docIndex([page(body)]);
    expect(entries[0].text).toBe('See code and a link and em and strong.');
    expect(entries[0].text).not.toContain('https://example.invalid');
    expect(entries[0].text).not.toContain('`');
    expect(entries[0].text).not.toContain('*');
  });

  it("includes a top-level bulleted list's text, including a nested sub-list's", () => {
    const body = '- one\n- two\n  - two deep\n    - two deeper\n- three\n';
    const entries = docIndex([page(body)]);
    expect(entries).toHaveLength(1);
    for (const word of ['one', 'two', 'two deep', 'two deeper', 'three']) {
      expect(entries[0].text).toContain(word);
    }
  });

  it("includes a table's header and cell text", () => {
    const body = '| Col A | Col B |\n| --- | --- |\n| cell one | cell two |\n';
    const entries = docIndex([page(body)]);
    expect(entries[0].text).toContain('Col A');
    expect(entries[0].text).toContain('cell one');
    expect(entries[0].text).toContain('cell two');
  });

  it('collapses whitespace so blocks join with a single space and no raw newline', () => {
    const entries = docIndex([page('Line one\nstill one block.\n\nSecond block.\n')]);
    expect(entries[0].text).toBe('Line one still one block. Second block.');
  });
});

describe('docIndex: anchors', () => {
  it("match renderDocs's ids, suffixes included, for a page with a repeated heading", () => {
    const body = '## Repeat\n\nOne.\n\n### Repeat\n\nTwo.\n\n## Repeat\n\nThree.\n';
    const doc = page(body);
    const entries = docIndex([doc]);
    const { ids } = renderDocs(doc.body);
    expect(entries.map((e) => e.anchor)).toEqual([null, ...ids]);
  });

  it('gives a depth-1, 5 or 6 heading no anchor and folds its text into the entry it sits in', () => {
    const body = '# Title\n\n## Real\n\nBody.\n\n###### Tiny\n\nMore.\n';
    const entries = docIndex([page(body)]);
    expect(entries).toHaveLength(2);
    expect(entries[0].text).toContain('Title');
    expect(entries[1]).toMatchObject({ heading: 'Real', anchor: 'real' });
    expect(entries[1].text).toContain('Tiny');
  });
});

describe('docIndex over DOC_PAGES', () => {
  const entries = docIndex();

  it('gives every page at least one entry', () => {
    const slugs = new Set(entries.map((e) => e.slug));
    for (const p of DOC_PAGES) expect(slugs.has(p.slug)).toBe(true);
  });

  it("gives every non-null anchor as one of that page's renderDocs ids", () => {
    const idsBySlug = new Map(DOC_PAGES.map((p) => [p.slug, renderDocs(p.body).ids]));
    for (const e of entries) {
      if (e.anchor === null) continue;
      expect(idsBySlug.get(e.slug)).toContain(e.anchor);
    }
  });

  it('is built once and memoised: a second call returns the same array', () => {
    expect(docIndex()).toBe(entries);
  });

  it("keeps the nested list in letting-the-loop-merge-for-you.md's one entry", () => {
    const own = entries.filter((e) => e.slug === 'letting-the-loop-merge-for-you');
    expect(own).toHaveLength(1);
    expect(own[0].text).toContain('bypass list');
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
});
