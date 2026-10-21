import { marked, type MarkedToken, type Tokens } from 'marked';
import { createSlugger, DOC_PAGES, plainText, type DocPage } from './docs';

/** One indexed section of a docs page: everything from one depth 2-4 heading up to
    the next (or the page's start, for the entry before its first heading, where
    `heading` and `anchor` are both null). */
export interface DocEntry {
  slug: string;
  title: string;
  summary: string;
  heading: string | null;
  anchor: string | null;
  text: string;
}

function isMermaid(lang: string | undefined): boolean {
  return (lang ?? '').trim().split(/\s+/)[0]?.toLowerCase() === 'mermaid';
}

/** The searchable text a single top-level (or list-item) token contributes. A
    `code` token is text with no inline tokens, skipped entirely when it is a
    mermaid fence (HA-657's source, not prose); `list` and `table` tokens carry
    their text one level down, in each item's or cell's own tokens, and are
    walked rather than read directly - a nested list recurses through this same
    function. Everything else that reaches the top of a docs page (space, hr,
    block html, a reference def) contributes nothing. */
function tokenText(token: MarkedToken): string {
  switch (token.type) {
    case 'paragraph':
    case 'heading':
    case 'blockquote':
      return plainText(token.tokens);
    case 'text':
      return token.tokens ? plainText(token.tokens) : token.text;
    case 'code':
      return isMermaid(token.lang) ? '' : token.text;
    case 'list':
      return token.items.map((item) => item.tokens.map((t) => tokenText(t as MarkedToken)).join(' ')).join(' ');
    case 'table': {
      const cells: Tokens.TableCell[] = [...token.header, ...token.rows.flat()];
      return cells.map((cell) => plainText(cell.tokens)).join(' ');
    }
    default:
      return '';
  }
}

/** One page, reduced to its entries: a new one opens at each depth 2-4 heading,
    sharing that page's slugger with renderDocs's own rule (depth < 2 or > 4 gets
    no id and its text joins whatever entry it sits in) so an anchor here is
    always one renderDocs would also assign. */
function pageEntries(page: DocPage): DocEntry[] {
  const slug = createSlugger();
  const entries: DocEntry[] = [];
  let heading: string | null = null;
  let anchor: string | null = null;
  let pieces: string[] = [];

  const flush = () => {
    entries.push({
      slug: page.slug,
      title: page.title,
      summary: page.summary,
      heading,
      anchor,
      text: pieces.join(' ').replace(/\s+/g, ' ').trim(),
    });
  };

  // marked.lexer's Token includes Tokens.Generic, a hook for a custom tokenizer
  // this file never installs - every token it actually hands back is one of
  // MarkedToken's named cases, which is what lets the switch above narrow on
  // `type` instead of widening every branch with Generic's optional fields.
  for (const token of marked.lexer(page.body) as MarkedToken[]) {
    if (token.type === 'heading' && token.depth >= 2 && token.depth <= 4) {
      flush();
      heading = plainText(token.tokens);
      anchor = slug(heading);
      pieces = [heading];
      continue;
    }
    pieces.push(tokenText(token));
  }
  flush();

  return entries;
}

function buildIndex(pages: DocPage[]): DocEntry[] {
  return pages.flatMap(pageEntries);
}

let memo: DocEntry[] | null = null;

/** The index, built on the first call and memoised thereafter - App.tsx imports
    DocsPage eagerly, so an eager build here would lex every page on every load
    of the app, board included (DocsPage.tsx states the same rule for the
    rendered pages). A `pages` argument bypasses the memo and is always
    rebuilt, which is what lets a test index a `DocPage` it wrote itself
    without disturbing the app's own index. */
export function docIndex(pages?: DocPage[]): DocEntry[] {
  if (pages) return buildIndex(pages);
  if (!memo) memo = buildIndex(DOC_PAGES);
  return memo;
}
