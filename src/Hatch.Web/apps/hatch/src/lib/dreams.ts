/* The Dreams page's data layer, apart from the page for the reason every
   other lib file here is: the web test run has no DOM, so what can be wrong
   lives in a function with a test beside it. */

import type { Dream, DreamWeaveRequest, Project, WeaveMode } from '../types';

/** The filter the Dreams page's controls hold. Every field but `loose` is a
    string, `''` meaning unset, the way search.ts's own filters are held.
    `loose` and `open` are real booleans - each is written to the URL as the
    literal string `'true'` or omitted, never `'false'`. `open` says the
    history (the search row and the list) is drawn; the page hides it until
    asked for. */
export interface DreamFilters {
  q: string;
  projectId: string;
  loose: boolean;
  issueKey: string;
  remandedFrom: string;
  open: boolean;
}

export const EMPTY_DREAMS: DreamFilters = {
  q: '',
  projectId: '',
  loose: false,
  issueKey: '',
  remandedFrom: '',
  open: false,
};

const POSITIVE_INTEGER = /^[1-9]\d*$/;

const idOrEmpty = (raw: string | null): string => (raw !== null && POSITIVE_INTEGER.test(raw) ? raw : '');

/** The filter, off the URL. `issueKey` is read from the `issue` param - the
    issue page's Drew-on section links to `/dreams?issue=KEY`, and that is
    the name on the address bar - with no validation, the same tolerance
    `readSearch` gives a bad `projectId`: an unparseable one just finds
    nothing, which is the server's job to decide, not this filter's.

    `open` is true for `open=true`, and also whenever the address carries
    `q`, `loose`, `issue` or `remandedFrom`: a link that narrows the list is a
    link that wants to see it. */
export function readDreams(params: URLSearchParams): DreamFilters {
  return {
    q: params.get('q') ?? '',
    projectId: idOrEmpty(params.get('projectId')),
    loose: params.get('loose') === 'true',
    issueKey: params.get('issue') ?? '',
    remandedFrom: params.get('remandedFrom') ?? '',
    open:
      params.get('open') === 'true' ||
      params.has('q') ||
      params.has('loose') ||
      params.has('issue') ||
      params.has('remandedFrom'),
  };
}

/** The fixed order every written query string holds its fields in, so equal
    filters are equal links. `issueKey` is written under `issue`, the URL's
    own name for it. */
const FIELD_ORDER: (keyof DreamFilters)[] = ['q', 'projectId', 'loose', 'issueKey', 'remandedFrom', 'open'];

/** The filter, onto a URL. Only set fields are written, in `FIELD_ORDER`;
    `q` is trimmed and omitted when it trims to nothing; `loose` is written
    as `'true'` only when true, and `open` the same, last. */
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
    } else if (field === 'issueKey') {
      if (filters.issueKey) params.set('issue', filters.issueKey);
    } else if (field === 'remandedFrom') {
      if (filters.remandedFrom) params.set('remandedFrom', filters.remandedFrom);
    } else {
      if (filters.open) params.set('open', 'true');
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

    With no project picked and a scope of `ids`, the scope is sent as a
    `projectId` list and `includeNoProject` is set, so a narrowed tab still
    sees the dreams that belong to no project. A picked project is already
    written by `writeDreams` and stays the lone `projectId`, with
    `includeNoProject` set too: the list holds its dreams and the ones that
    lean toward none. An empty `ids` with none picked adds nothing, which is
    the request an unscoped page has always made. */
export function dreamsRequest(filters: DreamFilters, offset: number, ids: number[]): URLSearchParams {
  const params = writeDreams(filters);
  // The server ignores it, and it would change the key the page reloads on.
  params.delete('open');
  const issue = params.get('issue');
  if (issue !== null) {
    params.delete('issue');
    params.set('issueKey', issue);
  }
  const remandedFrom = params.get('remandedFrom');
  if (remandedFrom !== null) {
    params.delete('remandedFrom');
    params.set('remandedFromKey', remandedFrom);
  }
  if (filters.projectId !== '') {
    params.set('includeNoProject', 'true');
  } else if (ids.length > 0) {
    for (const id of ids) params.append('projectId', String(id));
    params.set('includeNoProject', 'true');
  }
  params.set('limit', String(DREAMS_PAGE));
  if (offset !== 0) params.set('offset', String(offset));

  return params;
}

/** The request for how many dreams a project's choice holds: the list's own
    request with nothing but the picked project, and a page of one - the list
    route's `total` is the count, so no route of its own. */
export function dreamsCountRequest(projectId: string, ids: number[]): URLSearchParams {
  const params = dreamsRequest({ ...EMPTY_DREAMS, projectId }, 0, ids);
  params.set('limit', '1');

  return params;
}

/** Whether any filter is set, `open` aside, which the list being drawn already
    says: what tells "No dreams yet." from "Nothing matches." */
export function narrowed(filters: DreamFilters): boolean {
  return writeDreams({ ...filters, open: false }).size > 0;
}

/** The project filter, with a pick the scope no longer holds put back to
    `''`. A filter read off an old link can name a project the tab has since
    narrowed away from, and sending it would show a list the chooser cannot
    explain. An unscoped tab never drops anything: nobody narrowed it. */
export function scrubbedDreamProject(projectId: string, inScope: Project[], isScoped: boolean): string {
  if (!isScoped || projectId === '') return projectId;

  return inScope.some((p) => String(p.id) === projectId) ? projectId : '';
}

/** The picked project as an id: the address's `projectId` when `projects`
    holds it, else `null`. */
export function pickedProjectId(projectId: string, projects: Project[]): number | null {
  const parsed = Number(projectId);

  return projectId !== '' && projects.some((p) => p.id === parsed) ? parsed : null;
}

/** The line beside the picker, saying what the choice means for the
    composer, the weave and the list. With one project on the board, none
    picked still weaves, so the caption says where. */
export function projectCaption(projectId: string, projects: Project[]): string {
  const picked = pickedProjectId(projectId, projects);
  if (picked !== null) {
    const { key } = projects.find((p) => p.id === picked)!;
    return `New dreams lean toward ${key}. The weave files into it. The list holds its dreams and the ones that lean toward none.`;
  }

  const base = 'New dreams lean toward no project. The list covers every project.';

  return projects.length === 1 ? `${base} The weave files into the only project.` : `${base} Pick one to weave.`;
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

/** The "and N more" tail a chip of several keys adds after its root: empty
    for zero or one keys, else `and N more` with N the keys past the root. */
export function remandedMore(keys: string[]): string {
  return keys.length > 1 ? `and ${keys.length - 1} more` : '';
}

/** The question a dream's Delete button asks. A woven dream keeps today's
    wording; a remanded dream also says its issues stay where they are, named
    by their root the way the chip names it; a dream that is both joins the
    two sentences. Neither reads as the bare `Delete this dream?` once either
    list is non-empty. */
export function deleteDreamQuestion(dream: Pick<Dream, 'wovenInto' | 'remandedFrom'>): string {
  const sentences: string[] = [];

  if (dream.wovenInto.length > 0) {
    sentences.push(`It will also leave ${dream.wovenInto.join(', ')}'s "Drew on".`);
  }

  if (dream.remandedFrom.length > 0) {
    const [root] = dream.remandedFrom;
    const more = remandedMore(dream.remandedFrom);
    sentences.push(`The issues it was remanded from, ${root}${more ? ` ${more}` : ''}, stay where they are.`);
  }

  return sentences.length > 0 ? `Delete this dream? ${sentences.join(' ')}` : 'Delete this dream?';
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
    q, no issueKey, and either no projectId or a dream that is that project's
    or leans toward none, as the list's own request holds. `loose` never
    excludes it (a new dream has no issue woven in). `q` is judged trimmed,
    the way `writeDreams` writes it. */
export function showsDream(filters: DreamFilters, dream: Pick<Dream, 'projectId'>): boolean {
  if (filters.q.trim() !== '' || filters.issueKey !== '' || filters.remandedFrom !== '') return false;

  return filters.projectId === '' || dream.projectId === null || filters.projectId === String(dream.projectId);
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

/** Why Weave cannot be pressed, or `null` when it can: the line the page puts
    beside the button. In the order a person fixes them - no project exists,
    none is picked, then mode `seed` with nothing (once trimmed) to pull on.
    It is `null` exactly when `weaveRequest` is not, which the tests hold. */
export function weaveBlocker(args: {
  projectId: number | null;
  projectCount: number;
  mode: WeaveMode;
  seed: string | null;
  q: string;
}): string | null {
  const { projectId, projectCount, mode, seed, q } = args;
  if (projectId === null) {
    return projectCount === 0 ? 'There is no project to weave into yet.' : 'Pick a project above to weave.';
  }
  if (mode === 'seed' && weaveSeed(seed, q).trim() === '') return 'Name a thread to pull first.';

  return null;
}

/** The project a weave files into: the picked project, when it parses and
    names a project that still exists; else the only project there is, when
    there is exactly one; else `null`, so the person is asked rather than
    guessed for. */
export function weaveProjectDefault(filterProjectId: string, projects: Project[]): number | null {
  const parsed = Number(filterProjectId);
  if (filterProjectId !== '' && projects.some((project) => project.id === parsed)) return parsed;

  return projects.length === 1 ? projects[0].id : null;
}
