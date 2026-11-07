/* The Search page's data layer, apart from the page for the reason every
   other lib file here is: the web test run has no DOM, so what can be wrong
   lives in a function with a test beside it.

   It reads `SearchMatch` from ../types and copies none of it. */

import { ISSUE_TYPES, type SearchMatch } from '../types';

/** The filter the Search page's controls hold. Every field is a string,
    `''` meaning unset - what a `<select>` and an `<input type="date">` hold.
    Field names are the query parameters' own (SearchController.cs:58-67). */
export interface SearchFilters {
  q: string;
  projectId: string;
  type: string;
  statusId: string;
  createdFrom: string;
  createdTo: string;
  closedFrom: string;
  closedTo: string;
}

export const EMPTY_SEARCH: SearchFilters = {
  q: '',
  projectId: '',
  type: '',
  statusId: '',
  createdFrom: '',
  createdTo: '',
  closedFrom: '',
  closedTo: '',
};

const POSITIVE_INTEGER = /^[1-9]\d*$/;
const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;

const idOrEmpty = (raw: string | null): string => (raw !== null && POSITIVE_INTEGER.test(raw) ? raw : '');
const dateOrEmpty = (raw: string | null): string => (raw !== null && ISO_DATE.test(raw) ? raw : '');

/** The filter, off the URL. It never refuses: anything that does not read as
    its field's shape - a `projectId` with a sign or a leading zero, a `type`
    outside `ISSUE_TYPES`, a date not exactly `YYYY-MM-DD` - reads as `''`, so
    a hand-edited link never produces a 400. `q` is kept as typed, not
    trimmed. */
export function readSearch(params: URLSearchParams): SearchFilters {
  const type = params.get('type');

  return {
    q: params.get('q') ?? '',
    projectId: idOrEmpty(params.get('projectId')),
    type: type !== null && ISSUE_TYPES.includes(type as (typeof ISSUE_TYPES)[number]) ? type : '',
    statusId: idOrEmpty(params.get('statusId')),
    createdFrom: dateOrEmpty(params.get('createdFrom')),
    createdTo: dateOrEmpty(params.get('createdTo')),
    closedFrom: dateOrEmpty(params.get('closedFrom')),
    closedTo: dateOrEmpty(params.get('closedTo')),
  };
}

/** The fixed order every written query string holds its fields in, so equal
    searches are equal links. */
const FIELD_ORDER: (keyof SearchFilters)[] = [
  'q',
  'projectId',
  'type',
  'statusId',
  'createdFrom',
  'createdTo',
  'closedFrom',
  'closedTo',
];

/** The filter, onto a URL. Only non-empty fields are written, in
    `FIELD_ORDER`; `q` is trimmed and omitted when it trims to nothing. */
export function writeSearch(filters: SearchFilters): URLSearchParams {
  const params = new URLSearchParams();

  for (const field of FIELD_ORDER) {
    const value = field === 'q' ? filters.q.trim() : filters[field];
    if (value) params.set(field, value);
  }

  return params;
}

/** How many hits a page holds. */
export const SEARCH_PAGE = 50;

/** The request `getSearch` takes: `writeSearch(filters)` plus `limit`, plus
    `offset` when it is not the first page. With no project chosen, one
    `projectId` per id in `scopeIds` is appended, so a scoped tab searches only
    its projects; a chosen project is sent alone, and `scopeIds` is not
    consulted. */
export function searchRequest(filters: SearchFilters, offset: number, scopeIds: number[] = []): URLSearchParams {
  const params = writeSearch(filters);
  if (filters.projectId === '') {
    for (const id of scopeIds) params.append('projectId', String(id));
  }
  params.set('limit', String(SEARCH_PAGE));
  if (offset !== 0) params.set('offset', String(offset));

  return params;
}

/** `projectId` as the Project facet would actually offer it: itself when
    `offered` names that id, else `''` - including when `offered.length < 2`,
    since the facet is hidden then and a lit filter could never be cleared by
    hand. The id-keyed twin of `scrubbedProject`. */
export function scrubbedProjectId(projectId: string, offered: { id: number }[]): string {
  if (projectId === '') return '';
  if (offered.length < 2) return '';
  return offered.some((p) => String(p.id) === projectId) ? projectId : '';
}

/** Whether any field is set - `q` counting only when it trims to something -
    so the Reset button's condition is exactly "`writeSearch` would write
    something". */
export function isSearching(filters: SearchFilters): boolean {
  return writeSearch(filters).size > 0;
}

/** The results line: `"3 of 340"`, or `"0 results"` when nothing matched. */
export function countLine(shown: number, total: number): string {
  return total === 0 ? '0 results' : `${shown} of ${total}`;
}

/** Whether a next page exists. */
export function hasMore(shown: number, total: number): boolean {
  return shown < total;
}

/** The matched cell's words: the server's flags, joined, `work-log` worded as
    "work log". `''` for no words typed, which is what an empty `matched`
    list means. */
export function matchedWords(matched: SearchMatch[]): string {
  return matched.map((m) => (m === 'work-log' ? 'work log' : m)).join(', ');
}

/** The Done column head's link to the issues closed in it - the URL grammar
    for a column's own search, lived in one place. */
export function statusPath(statusId: number): string {
  return `/search?statusId=${statusId}`;
}

/** The OmniBar's trailing row's link to the Search page, for a typed prompt -
    `null` for a blank prompt, which is what leaves the row off. */
export function searchPath(query: string): string | null {
  const trimmed = query.trim();
  if (!trimmed) return null;

  return `/search?${new URLSearchParams({ q: trimmed })}`;
}
