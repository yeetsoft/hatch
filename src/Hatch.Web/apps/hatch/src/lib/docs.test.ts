import { describe, expect, it } from 'vitest';
import { marked } from 'marked';
import source from '../../public/hatch-at-home.md?raw';
import {
  createSlugger,
  docLinkTarget,
  type DocPage,
  DOC_PAGES,
  docNeighbours,
  docPage,
  docSections,
  DOC_SECTIONS,
  hashId,
  isPlainPrimaryClick,
  orderDocPages,
  pageForAnchor,
  parseDocPage,
  renderDocs,
  resolveDocHref,
} from './docs';

/** The document's lines outside fenced blocks, which is where its headings are. */
function proseLines(): string[] {
  const out: string[] = [];
  let fence: string | null = null;
  for (const line of source.split(/\r?\n/)) {
    const m = line.match(/^ {0,3}(`{3,}|~{3,})/);
    if (m) {
      if (fence === null) fence = m[1][0];
      else if (m[1][0] === fence) fence = null;
      continue;
    }
    if (fence === null) out.push(line);
  }
  return out;
}

const ids = (html: string) => [...html.matchAll(/<h[2-4] id="([^"]*)"/g)].map((m) => m[1]);

describe('renderDocs on the document', () => {
  const { html, outline } = renderDocs(source);

  it('lists the depth-2 headings in order, without backticks', () => {
    const expected = proseLines()
      .filter((l) => l.startsWith('## '))
      .map((l) => l.slice(3).replace(/`/g, ''));
    expect(outline.filter((e) => e.depth === 2).map((e) => e.text)).toEqual(expected);
  });

  it('gives every ##-#### heading a unique id', () => {
    const found = ids(html);
    expect(new Set(found).size).toBe(found.length);
    expect(found.length).toBe(proseLines().filter((l) => /^#{2,4} /.test(l)).length);
  });

  it('slugs the headings the document links to', () => {
    const outlineIds = outline.map((e) => e.id);
    for (const id of [
      'the-hatch-cli-command-by-command',
      'the-loop-hatch-go-to-work',
      'one-increment-hatch-work',
      'letting-the-loop-merge-for-you',
    ]) {
      expect(outlineIds).toContain(id);
    }
  });

  it('links each heading with a labelled anchor', () => {
    expect(html).toMatch(/<a class="hatch-docs-anchor" href="#the-hatch-cli-command-by-command" aria-label="[^"]+">#<\/a>/);
  });
});

describe('renderDocs on fixtures', () => {
  it('suffixes a repeated heading', () => {
    expect(renderDocs('## x\n\n## x\n').outline.map((e) => e.id)).toEqual(['x', 'x-1']);
    expect(renderDocs('## x\n\n### x\n\n## x\n').outline.map((e) => e.id)).toEqual(['x', 'x-1', 'x-2']);
  });

  it('slugs from the plain text of a heading', () => {
    const { outline } = renderDocs('## A *b* & `c`\n');
    expect(outline).toEqual([{ depth: 2, text: 'A b & c', id: 'a-b--c' }]);
  });

  it('escapes the label and keeps the markup in the heading', () => {
    const { html } = renderDocs('## Say "hi" <b>\n');
    expect(html).toContain('aria-label="Link to Say &quot;hi&quot; "');
  });

  it('leaves h1 and h5 to the default renderer', () => {
    const { html, outline } = renderDocs('# One\n\n##### Five\n');
    expect(html).not.toContain('id=');
    expect(outline).toEqual([]);
  });

  it('keeps #### out of the outline but gives it an id', () => {
    const { html, outline, ids } = renderDocs('#### d\n');
    expect(html).toContain('<h4 id="d">');
    expect(outline).toEqual([]);
    expect(ids).toEqual(['d']);
  });

  it('does not treat a # line in a fence as a heading', () => {
    const { html, outline } = renderDocs('```sh\n## not a heading\n```\n');
    expect(outline).toEqual([]);
    expect(ids(html)).toEqual([]);
  });

  it('renders a mermaid fence as a placeholder carrying its source', () => {
    const { html, outline, ids: headingIds, mermaidCount } = renderDocs('```mermaid\ngraph TD\n  A --> B\n```\n');
    const m = html.match(/<div class="hatch-docs-mermaid"><pre class="hatch-docs-mermaid-source">([\s\S]*?)<\/pre><\/div>/);
    expect(m).not.toBeNull();
    expect(m![1].replace(/&amp;/g, '&').replace(/&lt;/g, '<').replace(/&gt;/g, '>')).toBe('graph TD\n  A --> B');
    expect(outline).toEqual([]);
    expect(headingIds).toEqual([]);
    expect(mermaidCount).toBe(1);
  });

  it('counts two mermaid fences in one source', () => {
    expect(renderDocs('```mermaid\na\n```\n\n```mermaid\nb\n```\n').mermaidCount).toBe(2);
  });

  it('counts no mermaid fence at all as zero', () => {
    expect(renderDocs('no fence here\n').mermaidCount).toBe(0);
  });

  it('escapes a mermaid fence that carries markup', () => {
    const source = '<script>alert(1)</script>\n< > &';
    const { html } = renderDocs('```mermaid\n' + source + '\n```\n');
    expect(html).not.toContain('<script>');
    const m = html.match(/<pre class="hatch-docs-mermaid-source">([\s\S]*?)<\/pre>/);
    expect(m![1].replace(/&amp;/g, '&').replace(/&lt;/g, '<').replace(/&gt;/g, '>')).toBe(source);
  });

  it('leaves every non-mermaid fence to the default renderer', () => {
    for (const fenced of ['```sh\necho hi\n```\n', '```ts\nconst x = 1;\n```\n', '```\nplain\n```\n']) {
      const { html, mermaidCount } = renderDocs(fenced);
      expect(html).not.toContain('hatch-docs-mermaid');
      expect(mermaidCount).toBe(0);
    }
    expect(renderDocs('    indented\n').html).not.toContain('hatch-docs-mermaid');
  });

  it('matches mermaid case-insensitively and past trailing space, not mermaidjs', () => {
    expect(renderDocs('```Mermaid\nx\n```\n').html).toContain('hatch-docs-mermaid');
    expect(renderDocs('```mermaid \nx\n```\n').html).toContain('hatch-docs-mermaid');
    expect(renderDocs('```mermaidjs\nx\n```\n').html).not.toContain('hatch-docs-mermaid');
  });

  it('numbers nested headings in document order', () => {
    const { outline } = renderDocs('## a\n\n> ## a\n\n- ## a\n');
    expect(outline.map((e) => e.id)).toEqual(['a', 'a-1', 'a-2']);
  });

  it('leaves the global marked untouched', () => {
    renderDocs('## T\n');
    expect(marked.parse('## T', { async: false })).not.toContain('id=');
  });
});

describe('createSlugger', () => {
  it('cannot hand out a suffix twice', () => {
    const slug = createSlugger();
    expect([slug('x'), slug('x'), slug('x-1'), slug('x')]).toEqual(['x', 'x-1', 'x-1-1', 'x-2']);
  });

  it('keeps letters, digits, underscores and hyphens', () => {
    expect(createSlugger()('Ünï_c0de: a-b?')).toBe('ünï_c0de-a-b');
  });
});

describe('docLinkTarget', () => {
  it('reads a fragment, decoded', () => {
    expect(docLinkTarget('#x')).toEqual({ kind: 'fragment', id: 'x' });
    expect(docLinkTarget('#a%20b')).toEqual({ kind: 'fragment', id: 'a b' });
  });

  it('strips the app prefix from an app path', () => {
    expect(docLinkTarget('/apps/hatch/runner')).toEqual({ kind: 'app', to: '/runner' });
    expect(docLinkTarget('/apps/hatch')).toEqual({ kind: 'app', to: '/' });
    expect(docLinkTarget('/apps/hatch/')).toEqual({ kind: 'app', to: '/' });
    expect(docLinkTarget('/apps/hatch?a=1')).toEqual({ kind: 'app', to: '/?a=1' });
    expect(docLinkTarget('/apps/hatch/issues/HA-1?x=1#y')).toEqual({
      kind: 'app',
      to: '/issues/HA-1?x=1#y',
    });
  });

  it('leaves everything else to the browser', () => {
    expect(docLinkTarget('/apps/hatchery/x')).toEqual({ kind: 'external' });
    expect(docLinkTarget('https://cli.github.com/')).toEqual({ kind: 'external' });
    expect(docLinkTarget('//example.com/apps/hatch/x')).toEqual({ kind: 'external' });
    expect(docLinkTarget(null)).toEqual({ kind: 'external' });
    expect(docLinkTarget('')).toEqual({ kind: 'external' });
  });

  it('classifies every link in the real document', () => {
    const hrefs = [...renderDocs(source).html.matchAll(/<a (?![^>]*hatch-docs-anchor)[^>]*href="([^"]*)"/g)].map(
      (m) => m[1],
    );
    expect(hrefs.length).toBeGreaterThan(0);
    for (const href of hrefs) {
      const kind = docLinkTarget(href).kind;
      expect(['fragment', 'app', 'external']).toContain(kind);
      if (href.startsWith('/apps/hatch/')) expect(kind).toBe('app');
    }
  });
});

describe('resolveDocHref', () => {
  it('resolves the four page-link forms', () => {
    for (const slug of ['upgrading', 'hatch-at-home']) {
      for (const form of [slug, `${slug}.md`, `./${slug}`, `./${slug}.md`]) {
        expect(resolveDocHref(form)).toBe(`/apps/hatch/docs/${slug}`);
        expect(resolveDocHref(`${form}#rolling-back`)).toBe(`/apps/hatch/docs/${slug}#rolling-back`);
      }
    }
  });

  it('keeps a bare # as written', () => {
    expect(resolveDocHref('upgrading#')).toBe('/apps/hatch/docs/upgrading#');
  });

  it('returns everything else exactly as given', () => {
    for (const href of [
      '',
      '#x',
      '/abs',
      '//host/x',
      'https://cli.github.com/',
      'mailto:a@b.c',
      '../x',
      'a/b.md',
      'Upgrading.md',
      'a_b',
      'a.md.md',
      '.md',
      '././a',
      'upgrading.md?x=1',
      'upgrading?x=1#y',
      '/apps/hatch/runner',
    ]) {
      expect(resolveDocHref(href)).toBe(href);
    }
  });

  it('rewrites a page link in rendered output', () => {
    const { html } = renderDocs('[a](./upgrading.md#x)');
    expect(html).toContain('href="/apps/hatch/docs/upgrading#x"');
    expect(docLinkTarget('/apps/hatch/docs/upgrading#x')).toEqual({ kind: 'app', to: '/docs/upgrading#x' });
  });

  it('leaves an image source alone', () => {
    expect(renderDocs('![a](./upgrading.md)').html).toContain('src="./upgrading.md"');
  });
});

describe('renderDocs with a resolver', () => {
  it('defaults to the app address with no second argument', () => {
    expect(renderDocs('[a](./x)').html).toContain('href="/apps/hatch/docs/x"');
  });

  it('calls the resolver with the parsed slug and fragment, for all four link forms', () => {
    const resolveHref = (slug: string, fragment: string) => `R:${slug}:${fragment}`;
    for (const form of ['x', 'x.md', './x', './x.md']) {
      expect(renderDocs(`[a](${form})`, resolveHref).html).toContain('href="R:x:"');
      expect(renderDocs(`[a](${form}#y)`, resolveHref).html).toContain('href="R:x:#y"');
    }
  });

  it('never calls the resolver for anything else', () => {
    const resolveHref = (): string => {
      throw new Error('should not be called');
    };
    for (const href of [
      '',
      '#x',
      '/abs',
      '//host/x',
      'https://cli.github.com/',
      'mailto:a@b.c',
      '../x',
      'a/b.md',
      'Upgrading.md',
      'a_b',
      'a.md.md',
      '.md',
      '././a',
      'upgrading.md?x=1',
      'upgrading?x=1#y',
      '/apps/hatch/runner',
    ]) {
      expect(renderDocs(`[a](${href})`, resolveHref).html).toContain(`href="${href}"`);
    }
  });
});

describe('hashId', () => {
  it('is empty for no fragment', () => {
    expect(hashId('')).toBe('');
    expect(hashId('#')).toBe('');
  });

  it('returns a malformed escape as written', () => {
    expect(hashId('#%E0%A4%A')).toBe('%E0%A4%A');
  });
});

describe('isPlainPrimaryClick', () => {
  const click = { button: 0, metaKey: false, ctrlKey: false, shiftKey: false, altKey: false, defaultPrevented: false };

  it('takes an unmodified left click', () => {
    expect(isPlainPrimaryClick(click)).toBe(true);
  });

  it.each([
    { metaKey: true },
    { ctrlKey: true },
    { shiftKey: true },
    { altKey: true },
    { button: 1 },
    { defaultPrevented: true },
  ])('leaves %o to the browser', (change) => {
    expect(isPlainPrimaryClick({ ...click, ...change })).toBe(false);
  });
});

/** A page's raw text; a field set to `undefined` is left out. */
function page(fields: Record<string, string | undefined> = {}, body = 'Body.\n'): string {
  const all = { title: 'T', section: 'Concepts', order: '1', summary: 'S', ...fields };
  const lines = Object.entries(all)
    .filter(([, v]) => v !== undefined)
    .map(([k, v]) => `${k}: ${v}`);
  return ['---', ...lines, '---', body].join('\n');
}

describe('parseDocPage', () => {
  const path = '../../docs/x.md';

  it('reads the fields, and the slug from a glob key', () => {
    const raw = page({ title: 'Board and columns', section: 'Concepts', order: '2', summary: 'How it is laid out' });
    expect(parseDocPage('../../docs/board-and-columns.md', raw)).toEqual({
      slug: 'board-and-columns',
      title: 'Board and columns',
      section: 'Concepts',
      order: 2,
      summary: 'How it is laid out',
      body: 'Body.\n',
    });
  });

  it('accepts CRLF', () => {
    const raw = page();
    const crlf = parseDocPage(path, raw.replace(/\n/g, '\r\n'));
    expect(crlf).toEqual(parseDocPage(path, raw));
    expect(crlf.body).not.toContain('\r');
  });

  it('keeps a colon in the summary', () => {
    expect(parseDocPage(path, page({ summary: 'Use it: or not' })).summary).toBe('Use it: or not');
  });

  it('starts the body after the closing fence, dropping leading blank lines', () => {
    const { body } = parseDocPage(path, page({}, '\n\n# Heading\n\ntext\n'));
    expect(body).toBe('# Heading\n\ntext\n');
    expect(body).not.toContain('title:');
    expect(body).not.toContain('---');
  });

  it.each(['title', 'section', 'order', 'summary'])('throws, naming the file, when %s is missing', (key) => {
    expect(() => parseDocPage(path, page({ [key]: undefined }))).toThrow('x.md');
    expect(() => parseDocPage(path, page({ [key]: undefined }))).toThrow(`\`${key}\``);
  });

  it('throws on an unknown key', () => {
    expect(() => parseDocPage(path, page({ author: 'me' }))).toThrow('x.md');
  });

  it('throws on a repeated key', () => {
    const raw = page().replace('---\n', '---\ntitle: Again\n');
    expect(() => parseDocPage(path, raw)).toThrow('x.md');
  });

  it('throws on an empty value', () => {
    expect(() => parseDocPage(path, page({ summary: '' }))).toThrow('x.md');
  });

  it('throws on a line without a colon', () => {
    const raw = page().replace('---\n', '---\nnot a field\n');
    expect(() => parseDocPage(path, raw)).toThrow('x.md');
  });

  it.each(['0', '1.5', '-1', 'x'])('throws on order %s', (order) => {
    expect(() => parseDocPage(path, page({ order }))).toThrow('x.md');
  });

  it('throws without an opening fence', () => {
    expect(() => parseDocPage(path, '# Just a heading\n')).toThrow('x.md');
  });

  it('throws without a closing fence', () => {
    expect(() => parseDocPage(path, '---\ntitle: T\nsection: C\norder: 1\nsummary: S\n')).toThrow('x.md');
  });

  it.each(['Board.md', 'a b.md'])('throws on the slug in %s', (file) => {
    expect(() => parseDocPage(`../../docs/${file}`, page())).toThrow(file);
  });

  it('throws on a path that is not .md', () => {
    expect(() => parseDocPage('../../docs/x.txt', page())).toThrow('x.txt');
  });
});

/** A `DocPage` with everything but slug, section, order and body fixed. */
function dp(slug: string, section: string, order: number, body = 'Body.\n'): DocPage {
  return { slug, title: slug, section, order, summary: slug, body };
}

describe('orderDocPages', () => {
  it('sorts by section in DOC_SECTIONS order, then by order, without mutating the input', () => {
    const concepts2 = dp('concepts-2', 'Concepts', 2);
    const start1 = dp('start-1', 'Start here', 1);
    const concepts1 = dp('concepts-1', 'Concepts', 1);
    const input = [concepts2, start1, concepts1];
    expect(orderDocPages(input)).toEqual([start1, concepts1, concepts2]);
    expect(input).toEqual([concepts2, start1, concepts1]);
  });

  it('throws on a section outside DOC_SECTIONS, naming the file, the section and the five allowed', () => {
    const bad = dp('x', 'Nope', 1);
    expect(() => orderDocPages([bad])).toThrow('x.md');
    expect(() => orderDocPages([bad])).toThrow('Nope');
    for (const section of DOC_SECTIONS) {
      expect(() => orderDocPages([bad])).toThrow(section);
    }
  });

  it('throws on two pages sharing a section and an order, naming both files', () => {
    const a = dp('a', 'Concepts', 1);
    const b = dp('b', 'Concepts', 1);
    expect(() => orderDocPages([a, b])).toThrow('a.md');
    expect(() => orderDocPages([a, b])).toThrow('b.md');
  });

  it('allows two pages to share an order across different sections', () => {
    const a = dp('a', 'Start here', 1);
    const b = dp('b', 'Concepts', 1);
    expect(orderDocPages([a, b])).toEqual([a, b]);
  });
});

describe('docSections', () => {
  it('omits an empty section and keeps DOC_SECTIONS order', () => {
    const start = dp('start', 'Start here', 1);
    const reference = dp('reference', 'Reference', 1);
    const concepts = dp('concepts', 'Concepts', 1);
    expect(docSections([start, concepts, reference])).toEqual([
      { section: 'Start here', pages: [start] },
      { section: 'Concepts', pages: [concepts] },
      { section: 'Reference', pages: [reference] },
    ]);
  });
});

describe('docPage', () => {
  const a = dp('a', 'Start here', 1);
  const b = dp('b', 'Concepts', 1);

  it('finds the page with the slug', () => {
    expect(docPage([a, b], 'b')).toEqual(b);
  });

  it('is undefined for an unknown slug', () => {
    expect(docPage([a, b], 'c')).toBeUndefined();
  });
});

describe('pageForAnchor', () => {
  it.each([
    ['letting-the-loop-merge-for-you', '/docs/letting-the-loop-merge-for-you'],
    ['one-increment-hatch-work', '/docs/one-increment-and-the-loop#one-increment-hatch-work'],
    ['one-pass-in-order', '/docs/one-increment-and-the-loop#one-pass-in-order'],
    ['what-ios-cannot-do', '/docs/hatch-on-your-phone#what-ios-cannot-do'],
    ['prerequisites', '/docs/getting-started#prerequisites'],
    ['the-one-line', '/docs/getting-started#the-one-line'],
    ['how-to-stop', '/docs/deployment-overview#how-to-stop'],
    ['where-the-data-is-and-how-to-back-it-up', '/docs/backup-restore-and-replication#where-the-data-is-and-how-to-back-it-up'],
  ])('resolves %s to %s against the real pages', (id, to) => {
    expect(pageForAnchor(DOC_PAGES, id)).toBe(to);
  });

  it.each(['no-such-heading', ''])('is null for %s against the real pages', (id) => {
    expect(pageForAnchor(DOC_PAGES, id)).toBeNull();
  });

  it('finds a heading id the outline omits - a #### heading', () => {
    const a = dp('a', 'Start here', 1, '## Other\n');
    const b = dp('b', 'Concepts', 1, '#### Deep\n');
    expect(pageForAnchor([a, b], 'deep')).toBe('/docs/b#deep');
  });

  it('prefers the page whose slug is the id over a page with that heading', () => {
    const slugPage = dp('shared', 'Start here', 1, '## Other\n');
    const headingPage = dp('other', 'Concepts', 1, '## Shared\n');
    expect(pageForAnchor([slugPage, headingPage], 'shared')).toBe('/docs/shared');
  });
});

describe('docNeighbours', () => {
  it('crosses a section boundary, even over one with no pages', () => {
    const a = dp('a', 'Start here', 1);
    const b = dp('b', 'Running agents', 1);
    expect(docNeighbours([a, b], 'a')).toEqual({ previous: null, next: b });
    expect(docNeighbours([a, b], 'b')).toEqual({ previous: a, next: null });
  });

  it('gives null at both ends of the list', () => {
    const a = dp('a', 'Start here', 1);
    const b = dp('b', 'Start here', 2);
    const c = dp('c', 'Start here', 3);
    expect(docNeighbours([a, b, c], 'a')).toEqual({ previous: null, next: b });
    expect(docNeighbours([a, b, c], 'c')).toEqual({ previous: b, next: null });
  });

  it('gives null both ways for an unknown slug', () => {
    const a = dp('a', 'Start here', 1);
    expect(docNeighbours([a], 'nope')).toEqual({ previous: null, next: null });
  });
});

describe('DOC_PAGES', () => {
  it('is an array - empty until a page lands under apps/hatch/docs/', () => {
    expect(Array.isArray(DOC_PAGES)).toBe(true);
  });
});
