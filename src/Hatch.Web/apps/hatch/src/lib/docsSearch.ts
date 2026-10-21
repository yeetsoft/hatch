import type { DocEntry } from './docsIndex';
import type { Mark } from './goTo';

/** Lower-cased, with diacritics stripped, one code point in for one code point
    out - so an offset taken on folded text still indexes the original. Folding
    the whole string with one normalize('NFD') call would not do that: 'é' (one
    code point precomposed) decomposes to two ('e' plus a combining acute), so a
    single pass shifts every offset after the first accent. Each code point is
    folded on its own instead, and kept as itself whenever folding it changes its
    count - lower-casing or stripping marks from it would otherwise grow or
    shrink the string under an offset taken on the input. */
export function foldText(text: string): string {
  let out = '';
  for (const ch of text) {
    const lower = ch.toLowerCase();
    const base = [...lower].length === 1 ? lower : ch;
    const stripped = base.normalize('NFD').replace(/\p{M}/gu, '');
    out += [...stripped].length === 1 ? stripped : base;
  }
  return out;
}

/** How many rows `searchDocs` draws. */
export const DOC_SEARCH_LIMIT = 10;

/** One row of a search result: an entry that matched, with where it matched and
    the window of its text that shows why. */
export interface DocResult {
  slug: string;
  title: string;
  heading: string | null;
  anchor: string | null;
  where: 'title' | 'heading' | 'body';
  snippet: string;
  marks: Mark[];
}

/** The query, folded, split on whitespace, empties dropped. */
function searchTerms(query: string): string[] {
  return foldText(query).split(/\s+/).filter(Boolean);
}

/** Every occurrence of every term in `text`, sorted and merged - `titleMarks`'s
    algorithm (goTo.ts), re-written here because that one is not exported. */
function findMarks(text: string, terms: string[]): Mark[] {
  const found: Mark[] = [];
  for (const term of terms) {
    for (let at = text.indexOf(term); at !== -1; at = text.indexOf(term, at + 1)) {
      found.push({ start: at, end: at + term.length });
    }
  }

  found.sort((a, b) => a.start - b.start || a.end - b.end);
  const merged: Mark[] = [];
  for (const mark of found) {
    const last = merged[merged.length - 1];
    if (last && mark.start <= last.end) last.end = Math.max(last.end, mark.end);
    else merged.push({ ...mark });
  }
  return merged;
}

/** A window of `text` around its first occurrence of any term, roughly 160
    characters, cut back to a word boundary at each end (never into the match
    itself) with `…` where it was cut. Marks are relative to the returned
    snippet, not to `text`. */
function snippetAndMarks(text: string, terms: string[]): { snippet: string; marks: Mark[] } {
  const folded = foldText(text);
  let matchStart = -1;
  let matchEnd = -1;
  for (const term of terms) {
    const at = folded.indexOf(term);
    if (at !== -1 && (matchStart === -1 || at < matchStart)) {
      matchStart = at;
      matchEnd = at + term.length;
    }
  }

  const RADIUS = 80;
  let start = Math.max(0, (matchStart === -1 ? 0 : matchStart) - RADIUS);
  let end = Math.min(text.length, (matchEnd === -1 ? Math.min(text.length, RADIUS * 2) : matchEnd) + RADIUS);

  let prefix = '';
  let suffix = '';
  if (start > 0) {
    const space = text.indexOf(' ', start);
    if (space !== -1 && space < matchStart) start = space + 1;
    prefix = '…';
  }
  if (end < text.length) {
    const space = text.lastIndexOf(' ', end);
    if (space > matchEnd) end = space;
    suffix = '…';
  }

  const core = text.slice(start, end);
  const snippet = prefix + core + suffix;
  const marks = findMarks(foldText(core), terms).map((m) => ({
    start: m.start + prefix.length,
    end: m.end + prefix.length,
  }));
  return { snippet, marks };
}

/** The entries `searchDocs` would return a result for, each with where it
    matched. One page that matches on its title contributes only its page
    entry (the heading-less one every page opens with, per `pageEntries`) -
    every other matching entry of that page is dropped, so a page with ten
    matching sections cannot fill all ten rows itself. */
function matchEntries(entries: DocEntry[], terms: string[]): { entry: DocEntry; where: DocResult['where'] }[] {
  const titleMatchSlugs = new Set<string>();
  for (const entry of entries) {
    if (titleMatchSlugs.has(entry.slug)) continue;
    if (terms.every((term) => foldText(entry.title).includes(term))) titleMatchSlugs.add(entry.slug);
  }

  const matches: { entry: DocEntry; where: DocResult['where'] }[] = [];
  for (const entry of entries) {
    if (titleMatchSlugs.has(entry.slug)) {
      if (entry.heading === null) matches.push({ entry, where: 'title' });
      continue;
    }

    const foldedHeading = entry.heading !== null ? foldText(entry.heading) : null;
    const inHeading = foldedHeading !== null && terms.every((term) => foldedHeading.includes(term));
    const inText = terms.every((term) => foldText(entry.text).includes(term));
    if (!inHeading && !inText) continue;
    matches.push({ entry, where: inHeading ? 'heading' : 'body' });
  }
  return matches;
}

const WHERE_RANK: Record<DocResult['where'], number> = { title: 0, heading: 1, body: 2 };

/**
 * The docs search's whole query: every entry whose folded title, heading or
 * body text holds every folded term, at most `DOC_SEARCH_LIMIT` of them,
 * `total` the count before that cap.
 *
 * Plain substring matching, deliberately: it already gives a trailing partial
 * word for free (`go-to-w` is a substring of `go-to-work`), so there is no
 * prefix index, no stemming and no edit distance here. If that stops being
 * enough, build one - this module does not.
 */
export function searchDocs(query: string, entries: DocEntry[]): { results: DocResult[]; total: number } {
  const terms = searchTerms(query);
  if (terms.length === 0) return { results: [], total: 0 };

  const matches = matchEntries(entries, terms)
    .map((m, index) => ({ ...m, index }))
    .sort((a, b) => WHERE_RANK[a.where] - WHERE_RANK[b.where] || a.index - b.index);

  const results = matches.slice(0, DOC_SEARCH_LIMIT).map(({ entry, where }): DocResult => {
    const { snippet, marks } = snippetAndMarks(where === 'title' ? entry.summary : entry.text, terms);
    return {
      slug: entry.slug,
      title: entry.title,
      heading: where === 'title' ? null : entry.heading,
      anchor: where === 'title' ? null : entry.anchor,
      where,
      snippet,
      marks,
    };
  });

  return { results, total: matches.length };
}

/** The id a result is held and announced by: stable across a re-run of the
    same query, which is what lets `aria-activedescendant` and the highlight
    survive a re-render. Held by id rather than by index for `keepHighlight`'s
    reason (goTo.ts): a row's index moves under a result set that just
    changed, the thing a highlight must not do. */
export function resultId(result: DocResult): string {
  return `${result.slug}#${result.anchor ?? ''}`;
}

/** The result to highlight once `results` has changed: the same one if it is
    still listed, else the first result's, else null. `keepHighlight`'s shape
    (goTo.ts). */
export function keepDocHighlight(results: DocResult[], id: string | null): string | null {
  if (id !== null && results.some((r) => resultId(r) === id)) return id;
  return results[0] ? resultId(results[0]) : null;
}

/** The id `delta` rows away from `id`, clamped into the first and last result
    rather than wrapping - a list this short has no use for the wraparound a
    long one might. */
export function moveDocHighlight(results: DocResult[], id: string | null, delta: number): string | null {
  if (results.length === 0) return null;

  const current = id === null ? -1 : results.findIndex((r) => resultId(r) === id);
  const target = Math.min(results.length - 1, Math.max(0, current + delta));
  return resultId(results[target]);
}

/** The status line's left half: nothing for a blank query, "no page matches"
    for terms nothing holds, the capped count while more are hidden, else the
    plain count. `goToStatus`'s shape (goTo.ts). */
export function docSearchStatus(query: string, shown: number, total: number): string {
  if (!query.trim()) return '';
  if (total === 0) return 'no page matches';
  if (shown < total) return `${shown} of ${total} — keep typing`;
  return total === 1 ? '1 page' : `${total} results`;
}

/** A copy of `params` with `q` set to `q` as typed (not trimmed: `?q=%20` is a
    legal address and the field shows it), or removed when `q` is empty rather
    than left as `?q=`. Every other param is kept; the input is never mutated. */
export function withDocQuery(params: URLSearchParams, q: string): URLSearchParams {
  const next = new URLSearchParams(params);
  if (q === '') next.delete('q');
  else next.set('q', q);

  return next;
}

/** Where taking `result` goes: its page, `params` with `q` set to `q` (so the
    field still holds the query on the page the reader lands on, and any other
    param stays), then its anchor. URLSearchParams writes a space as `+` and
    `#` as `%23` and reads both back exactly, so a query cannot end the search
    early or corrupt the fragment. `page` addresses the page itself, defaulting
    to the app's own `/docs/<slug>` shape - the site names its own. */
export function docTarget(
  result: DocResult,
  params: URLSearchParams,
  q: string,
  page: (slug: string) => string = (slug) => `/docs/${slug}`,
): string {
  const search = withDocQuery(params, q).toString();

  return `${page(result.slug)}${search ? `?${search}` : ''}${result.anchor ? `#${result.anchor}` : ''}`;
}
