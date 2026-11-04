/* The Dreams page's data layer, apart from the page for the reason every
   other lib file here is: the web test run has no DOM, so what can be wrong
   lives in a function with a test beside it. */

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
    plus `offset` when it is not the first page. */
export function dreamsRequest(filters: DreamFilters, offset: number): URLSearchParams {
  const params = writeDreams(filters);
  const issue = params.get('issue');
  if (issue !== null) {
    params.delete('issue');
    params.set('issueKey', issue);
  }
  params.set('limit', String(DREAMS_PAGE));
  if (offset !== 0) params.set('offset', String(offset));

  return params;
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
