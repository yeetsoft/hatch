import { Marked, type Token, type Tokens } from 'marked';
import { APP_BASENAME } from './basename';

/** The plain text of a run of inline tokens: what a reader sees, with the markup
    gone. A heading token's own `text` is raw markdown (backticks and all), so a
    slug taken from it would carry the markup's punctuation. Leaf `text` is
    already unescaped, so nothing here decodes entities. */
export function plainText(tokens: Token[]): string {
  let out = '';
  for (const token of tokens) {
    switch (token.type) {
      case 'text':
      case 'codespan':
      case 'escape':
        out += token.text;
        break;
      case 'image':
        out += token.text;
        break;
      case 'html':
        break;
      default:
        if ('tokens' in token && Array.isArray(token.tokens)) out += plainText(token.tokens);
    }
  }
  return out;
}

/** GitHub's heading ids: lowercase, punctuation dropped, spaces to hyphens, and a
    repeat takes `-1`, `-2`, ... The next suffix is tried until the candidate is
    unused, so `x`, `x`, `x-1` cannot hand out `x-1` twice. */
export function createSlugger(): (text: string) => string {
  const uses = new Map<string, number>();
  return (text) => {
    const base = text.toLowerCase().replace(/[^\p{L}\p{N} _-]/gu, '').replace(/ /g, '-');
    let candidate = base;
    let n = uses.get(base) ?? 0;
    while (uses.has(candidate)) candidate = `${base}-${++n}`;
    uses.set(base, n);
    uses.set(candidate, uses.get(candidate) ?? 0);
    return candidate;
  };
}

export interface OutlineEntry {
  depth: 2 | 3;
  text: string;
  id: string;
}

function escapeAttr(s: string): string {
  return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

/** Renders the document to HTML whose `##`-`####` headings carry an id and a `#`
    anchor, and lists the `##`/`###` headings as an outline. One pass: the outline
    and the ids come from the same slugger run in document order, so they cannot
    disagree. The output is not sanitized - that is the page's job, and a separate
    step. Links to other pages resolve to `/docs/<slug>` addresses. A local
    `Marked`, so the global instance `markdown.ts` uses is untouched. */
export function renderDocs(
  source: string,
): { html: string; outline: OutlineEntry[]; ids: string[]; mermaidCount: number } {
  const slug = createSlugger();
  const outline: OutlineEntry[] = [];
  const ids: string[] = [];
  let mermaidCount = 0;
  const marked = new Marked({
    walkTokens(token) {
      if (token.type === 'link') token.href = resolveDocHref(token.href);
    },
    renderer: {
      heading(token: Tokens.Heading) {
        const { depth, tokens } = token;
        if (depth < 2 || depth > 4) return false;
        const text = plainText(tokens);
        const id = slug(text);
        ids.push(id);
        if (depth === 2 || depth === 3) outline.push({ depth, text, id });
        const label = escapeAttr(text);
        return (
          `<h${depth} id="${id}">${this.parser.parseInline(tokens)}` +
          `<a class="hatch-docs-anchor" href="#${id}" aria-label="Link to ${label}">#</a></h${depth}>\n`
        );
      },
      code({ text, lang }: Tokens.Code) {
        const word = (lang ?? '').trim().split(/\s+/)[0]?.toLowerCase();
        if (word !== 'mermaid') return false;
        mermaidCount++;
        return `<div class="hatch-docs-mermaid"><pre class="hatch-docs-mermaid-source">${escapeAttr(text)}</pre></div>`;
      },
    },
  });
  const html = marked.parse(source, { async: false });
  return { html, outline, ids, mermaidCount };
}

/** The id a `#fragment` names, decoded: one leading `#` off, then percent-escapes
    undone. A malformed escape is not an error worth a throw - the text comes back
    as written and simply matches no heading. */
export function hashId(hash: string): string {
  const raw = hash.startsWith('#') ? hash.slice(1) : hash;
  try {
    return decodeURIComponent(raw);
  } catch {
    return raw;
  }
}

export type DocLinkTarget =
  | { kind: 'fragment'; id: string }
  | { kind: 'app'; to: string }
  | { kind: 'external' };

/** What a link inside the document is: a fragment in this page, a path within this
    app (handed to the router as `to`, the app prefix stripped), or anything else,
    which is the browser's. The prefix is matched by whole segment, the rule
    `routerBasename` uses, so `/apps/hatchery/x` is not this app. */
export function docLinkTarget(href: string | null): DocLinkTarget {
  if (!href) return { kind: 'external' };
  if (href.startsWith('#')) return { kind: 'fragment', id: hashId(href) };
  if (href.startsWith(APP_BASENAME)) {
    const rest = href.slice(APP_BASENAME.length);
    if (rest === '' || rest[0] === '/' || rest[0] === '?' || rest[0] === '#') {
      return { kind: 'app', to: rest[0] === '/' ? rest : `/${rest}` };
    }
  }
  return { kind: 'external' };
}

/** A click the page may take over: the primary button, unmodified, and not already
    handled. Anything else is the reader asking the browser for something. */
export function isPlainPrimaryClick(e: {
  button: number;
  metaKey: boolean;
  ctrlKey: boolean;
  shiftKey: boolean;
  altKey: boolean;
  defaultPrevented: boolean;
}): boolean {
  return e.button === 0 && !e.metaKey && !e.ctrlKey && !e.shiftKey && !e.altKey && !e.defaultPrevented;
}

/** One page of the docs: the file's frontmatter, and the markdown after it. */
export interface DocPage {
  slug: string;
  title: string;
  section: string;
  order: number;
  summary: string;
  body: string;
}

const DOC_KEYS = ['title', 'section', 'order', 'summary'] as const;
const SLUG = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;

/** Where a link to another docs page goes: `slug`, `slug.md`, `./slug` or
    `./slug.md`, with an optional `#fragment`, become `${APP_BASENAME}/docs/slug`
    with the fragment kept as written. Anything else - a scheme, an absolute or
    parent path, a query, a name that is not a slug - comes back unchanged. */
export function resolveDocHref(href: string): string {
  const hash = href.indexOf('#');
  const path = hash === -1 ? href : href.slice(0, hash);
  const fragment = hash === -1 ? '' : href.slice(hash);
  let name = path.startsWith('./') ? path.slice(2) : path;
  if (name.endsWith('.md')) name = name.slice(0, -'.md'.length);
  return SLUG.test(name) ? `${APP_BASENAME}/docs/${name}${fragment}` : href;
}

/** Reads one markdown file, as the glob hands it over, into a page. Every way the
    file can be wrong throws and names `path`, so a bad page fails the build at the
    file to fix rather than rendering as a blank. The body is returned LF-only, with
    the frontmatter gone, so Marked never sees it. */
export function parseDocPage(path: string, raw: string): DocPage {
  const fail = (why: string): never => {
    throw new Error(`${path}: ${why}`);
  };

  const file = path.slice(path.lastIndexOf('/') + 1);
  if (!file.endsWith('.md')) fail('a docs page must be a .md file');
  const slug = file.slice(0, -'.md'.length);
  if (!SLUG.test(slug)) fail(`the file name "${slug}" must be lowercase words joined by hyphens`);

  const lines = raw.split(/\r?\n/);
  if (lines[0] !== '---') fail('must open with a --- frontmatter line');
  const close = lines.indexOf('---', 1);
  if (close === -1) fail('frontmatter is not closed by a --- line');

  const fields = new Map<string, string>();
  for (const line of lines.slice(1, close)) {
    const colon = line.indexOf(':');
    if (colon === -1) fail(`frontmatter line "${line}" is not "key: value"`);
    const key = line.slice(0, colon).trim();
    const value = line.slice(colon + 1).trim();
    if (!(DOC_KEYS as readonly string[]).includes(key)) fail(`unknown frontmatter key "${key}"`);
    if (fields.has(key)) fail(`frontmatter key "${key}" appears more than once`);
    if (value === '') fail(`frontmatter key "${key}" has no value`);
    fields.set(key, value);
  }
  for (const key of DOC_KEYS) {
    if (!fields.has(key)) fail(`frontmatter is missing \`${key}\``);
  }

  const order = fields.get('order')!;
  if (!/^[1-9]\d*$/.test(order)) fail(`order "${order}" must be a positive whole number`);

  return {
    slug,
    title: fields.get('title')!,
    section: fields.get('section')!,
    order: Number(order),
    summary: fields.get('summary')!,
    body: lines
      .slice(close + 1)
      .join('\n')
      .replace(/^\n+/, ''),
  };
}

/** The order a reader sees sections in - the one place section order is written. */
export const DOC_SECTIONS = [
  'Start here',
  'Concepts',
  'Running agents',
  'Deploying and operating',
  'Reference',
] as const;

export type DocSection = (typeof DOC_SECTIONS)[number];

// `DOC_SECTIONS.indexOf` does not compile under strict: a DocPage's `section`
// is `string`, and `indexOf`'s parameter is typed to the literal union.
function sectionIndex(section: string): number {
  return DOC_SECTIONS.findIndex((s) => s === section);
}

/** Sorts a copy of `pages` by section (in DOC_SECTIONS order), then by `order`,
    leaving `pages` itself untouched. Throws on a section outside DOC_SECTIONS, or
    on two pages sharing a section and an order - `DocPage` keeps no path, so each
    error names `${slug}.md`. */
export function orderDocPages(pages: DocPage[]): DocPage[] {
  for (const p of pages) {
    if (sectionIndex(p.section) === -1) {
      throw new Error(`${p.slug}.md: section "${p.section}" must be one of ${DOC_SECTIONS.join(', ')}`);
    }
  }
  const sorted = [...pages].sort((a, b) => {
    const bySection = sectionIndex(a.section) - sectionIndex(b.section);
    return bySection !== 0 ? bySection : a.order - b.order;
  });
  // sort is stable (guaranteed since ES2019) and the comparator orders by section
  // then by order, so two pages sharing both land adjacent to each other.
  for (let i = 1; i < sorted.length; i++) {
    const prev = sorted[i - 1];
    const curr = sorted[i];
    if (prev.section === curr.section && prev.order === curr.order) {
      throw new Error(`${prev.slug}.md and ${curr.slug}.md share section "${curr.section}" and order ${curr.order}`);
    }
  }
  return sorted;
}

/** Each section with at least one page, in `DOC_SECTIONS` order. Relies on `pages`
    already being ordered - `DOC_PAGES`, or the output of `orderDocPages`. */
export function docSections(pages: DocPage[]): { section: DocSection; pages: DocPage[] }[] {
  return DOC_SECTIONS.map((section) => ({
    section,
    pages: pages.filter((p) => p.section === section),
  })).filter((s) => s.pages.length > 0);
}

/** The page with this slug, or undefined. */
export function docPage(pages: DocPage[], slug: string): DocPage | undefined {
  return pages.find((p) => p.slug === slug);
}

/** Where an old `/docs#id` fragment lands now: `/docs/<slug>` for the page whose
    slug is `id` (most of the pre-split links, since the split pages' slugs are
    the old page's `##` ids), else `/docs/<slug>#id` for the page holding a
    heading with that id, else `null` when nothing does. Slugs across all pages
    are checked before any heading, so the common case costs no render; headings
    are rendered lazily, one page at a time, stopping at the first match. */
export function pageForAnchor(pages: DocPage[], id: string): string | null {
  if (!id) return null;
  for (const page of pages) {
    if (page.slug === id) return `/docs/${page.slug}`;
  }
  for (const page of pages) {
    if (renderDocs(page.body).ids.includes(id)) return `/docs/${page.slug}#${id}`;
  }
  return null;
}

/** The pages either side of `slug` in reading order, walking the sorted list end to
    end across sections. Both `null` for a slug not in `pages`. */
export function docNeighbours(pages: DocPage[], slug: string): { previous: DocPage | null; next: DocPage | null } {
  const i = pages.findIndex((p) => p.slug === slug);
  if (i === -1) return { previous: null, next: null };
  return { previous: pages[i - 1] ?? null, next: pages[i + 1] ?? null };
}

// apps/hatch/docs/ does not exist yet, so the glob matches nothing and this is `[]`.
// A bad page throws here, at module load - every test importing lib/docs fails with
// the file's name until it is fixed.
export const DOC_PAGES = orderDocPages(
  Object.entries(
    import.meta.glob('../../docs/*.md', { query: '?raw', import: 'default', eager: true }) as Record<string, string>,
  ).map(([path, raw]) => parseDocPage(path, raw)),
);
