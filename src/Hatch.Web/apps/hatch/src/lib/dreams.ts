/* The Dreams page's data layer, apart from the page for the reason every
   other lib file here is: the web test run has no DOM, so what can be wrong
   lives in a function with a test beside it. */

import type { Dream, DreamWeaveRequest, Project, WeaveMode } from '../types';

/** The filter the Dreams page's controls hold. Every field but `loose` is a
    string, `''` meaning unset, the way search.ts's own filters are held.
    `loose` is a real boolean - it is written to the URL as the literal
    string `'true'` or omitted, never `'false'`. */
export interface DreamFilters {
  q: string;
  projectId: string;
  loose: boolean;
  issueKey: string;
}

export const EMPTY_DREAMS: DreamFilters = {
  q: '',
  projectId: '',
  loose: false,
  issueKey: '',
};

const POSITIVE_INTEGER = /^[1-9]\d*$/;

const idOrEmpty = (raw: string | null): string => (raw !== null && POSITIVE_INTEGER.test(raw) ? raw : '');

/** The filter, off the URL. `issueKey` is read from the `issue` param - the
    issue page's Drew-on section links to `/dreams?issue=KEY`, and that is
    the name on the address bar - with no validation, the same tolerance
    `readSearch` gives a bad `projectId`: an unparseable one just finds
    nothing, which is the server's job to decide, not this filter's. */
export function readDreams(params: URLSearchParams): DreamFilters {
  return {
    q: params.get('q') ?? '',
    projectId: idOrEmpty(params.get('projectId')),
    loose: params.get('loose') === 'true',
    issueKey: params.get('issue') ?? '',
  };
}

/** The fixed order every written query string holds its fields in, so equal
    filters are equal links. `issueKey` is written under `issue`, the URL's
    own name for it. */
const FIELD_ORDER: (keyof DreamFilters)[] = ['q', 'projectId', 'loose', 'issueKey'];

/** The filter, onto a URL. Only set fields are written, in `FIELD_ORDER`;
    `q` is trimmed and omitted when it trims to nothing; `loose` is written
    as `'true'` only when true. */
export function writeDreams(filters: DreamFilters): URLSearchParams {
  const params = new URLSearchParams();

  for (const field of FIELD_ORDER) {
    if (field === 'q') {
      const value = filters.q.trim();
      if (value) params.set('q', value);
    } else if (field === 'projectId') {
      if (filters.projectId) params.set('projectId', filters.projectId);
    } else if (field === 'loose') {
      if (filters.loose) params.set('loose', 'true');
    } else {
      if (filters.issueKey) params.set('issue', filters.issueKey);
    }
  }

  return params;
}

/** How many dreams a page holds - the server's own `DefaultLimit` in
    DreamsController.cs. */
export const DREAMS_PAGE = 50;

/** The request `getDreams` takes: `writeDreams(filters)`, with its `issue`
    key renamed to the server's own `issueKey` query parameter, plus `limit`,
    plus `offset` when it is not the first page.

    With no project chosen and a scope of `ids`, the scope is sent as a
    `projectId` list and `includeNoProject` is set, so a narrowed tab still
    sees the dreams that belong to no project. A chosen project is already
    written by `writeDreams` and stays the lone `projectId`; an empty `ids`
    adds nothing, which is the request an unscoped page has always made. */
export function dreamsRequest(filters: DreamFilters, offset: number, ids: number[]): URLSearchParams {
  const params = writeDreams(filters);
  const issue = params.get('issue');
  if (issue !== null) {
    params.delete('issue');
    params.set('issueKey', issue);
  }
  if (filters.projectId === '' && ids.length > 0) {
    for (const id of ids) params.append('projectId', String(id));
    params.set('includeNoProject', 'true');
  }
  params.set('limit', String(DREAMS_PAGE));
  if (offset !== 0) params.set('offset', String(offset));

  return params;
}

/** The project filter, with a pick the scope no longer holds put back to
    `''`. A filter read off an old link can name a project the tab has since
    narrowed away from, and sending it would show a list the chooser cannot
    explain. An unscoped tab never drops anything: nobody narrowed it. */
export function scrubbedDreamProject(projectId: string, inScope: Project[], isScoped: boolean): string {
  if (!isScoped || projectId === '') return projectId;

  return inScope.some((p) => String(p.id) === projectId) ? projectId : '';
}

/** The project a lean or weave pick offers, if the scope still holds it; else
    `null`. A pick left over from a scope that has since narrowed is dropped
    here rather than sent. */
export function offeredProjectId(id: number | null, inScope: Project[]): number | null {
  return id !== null && inScope.some((p) => p.id === id) ? id : null;
}

/** A dream's one-liner: its body's first line, trimmed - the same one-liner
    changelog.ts's own `fixLine` already uses for a release note. */
export function firstLine(body: string): string {
  return body.split('\n')[0].trim();
}

/** Whether `source` reads as a web address worth linking: it parses and its
    protocol is `http:` or `https:`. A `javascript:` or `mailto:` source parses
    as a URL too, and must stay plain text. */
export function isUrl(source: string): boolean {
  try {
    const { protocol } = new URL(source);
    return protocol === 'http:' || protocol === 'https:';
  } catch {
    return false;
  }
}

/** The count line under the filters: "3 of 340", or "0 results" when nothing
    matched. Copied from search.ts, not imported - the two files are
    deliberately separate. */
export function countLine(shown: number, total: number): string {
  return total === 0 ? '0 results' : `${shown} of ${total}`;
}

/** Whether a next page exists. */
export function hasMore(shown: number, total: number): boolean {
  return shown < total;
}

/** Whether a freshly posted dream belongs in the list the filters show: no
    q, no issueKey, and either no projectId or the dream's own. `loose` never
    excludes it (a new dream has no issue woven in). `q` is judged trimmed,
    the way `writeDreams` writes it. */
export function showsDream(filters: DreamFilters, dream: Pick<Dream, 'projectId'>): boolean {
  if (filters.q.trim() !== '' || filters.issueKey !== '') return false;

  return filters.projectId === '' || filters.projectId === String(dream.projectId);
}

/** The list with `updated` in place of the dream sharing its id, order kept.
    An id the list does not hold changes nothing. */
export function replaceDream(dreams: Dream[], updated: Dream): Dream[] {
  return dreams.map((dream) => (dream.id === updated.id ? updated : dream));
}

/** The list without the dream `id`. An id the list does not hold changes nothing. */
export function removeDream(dreams: Dream[], id: number): Dream[] {
  return dreams.filter((dream) => dream.id !== id);
}

/** The four ways a weave press can draw its epic, in the order the control offers them. */
export const WEAVE_MODES: readonly WeaveMode[] = ['seed', 'pressing', 'common', 'next'];

/** The seed box's text: the person's own once they have typed - `seed` is no
    longer `null` the moment the box is touched, even to clear it - else the
    search box's own `q`, so the seed follows a search the person is already
    narrowing until they start one of their own. */
export function weaveSeed(seed: string | null, q: string): string {
  return seed !== null ? seed : q;
}

/** What a weave press sends, or `null` while it cannot be pressed: no project
    chosen, or mode `seed` with nothing (once trimmed) in the seed box. `seed`
    is sent, trimmed, only in mode `seed` - the other modes draw their own
    text server-side and a leftover seed must not reach them. `dreamIds` is
    sent, in list order, only when the on-screen box is asked for and the
    list is not empty. */
export function weaveRequest(args: {
  mode: WeaveMode;
  seed: string | null;
  q: string;
  projectId: number | null;
  useOnScreen: boolean;
  dreams: Dream[];
}): DreamWeaveRequest | null {
  const { mode, seed, q, projectId, useOnScreen, dreams } = args;
  if (projectId === null) return null;

  const trimmedSeed = weaveSeed(seed, q).trim();
  if (mode === 'seed' && trimmedSeed === '') return null;

  const request: DreamWeaveRequest = { projectId, mode };
  if (mode === 'seed') request.seed = trimmedSeed;
  if (useOnScreen && dreams.length > 0) request.dreamIds = dreams.map((dream) => dream.id);

  return request;
}

/** The project a weave starts on: the Project facet's own, when it parses and
    names a project that still exists; else the only project there is, when
    there is exactly one; else `null`, so the person is asked rather than
    guessed for. */
export function weaveProjectDefault(filterProjectId: string, projects: Project[]): number | null {
  const parsed = Number(filterProjectId);
  if (filterProjectId !== '' && projects.some((project) => project.id === parsed)) return parsed;

  return projects.length === 1 ? projects[0].id : null;
}
