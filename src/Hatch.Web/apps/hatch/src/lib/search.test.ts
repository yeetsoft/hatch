import { describe, expect, it } from 'vitest';
import {
  EMPTY_SEARCH,
  SEARCH_PAGE,
  countLine,
  hasMore,
  isSearching,
  matchedWords,
  readSearch,
  scrubbedProjectId,
  searchPath,
  searchRequest,
  statusPath,
  writeSearch,
  type SearchFilters,
} from './search';

function params(entries: Record<string, string>): URLSearchParams {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(entries)) params.set(key, value);
  return params;
}

describe('readSearch', () => {
  it('reads an empty params as the empty filter', () => {
    expect(readSearch(params({}))).toEqual(EMPTY_SEARCH);
  });

  it('reads q as typed, not trimmed', () => {
    expect(readSearch(params({ q: '  needle  ' })).q).toBe('  needle  ');
  });

  it('reads a positive integer projectId as itself', () => {
    expect(readSearch(params({ projectId: '12' })).projectId).toBe('12');
  });

  it.each(['0', '-1', '1.5', 'abc'])('reads an invalid projectId %s as empty', (raw) => {
    expect(readSearch(params({ projectId: raw })).projectId).toBe('');
  });

  it('reads a leading-zero projectId as empty', () => {
    expect(readSearch(params({ projectId: '01' })).projectId).toBe('');
  });

  it('reads a positive integer statusId as itself', () => {
    expect(readSearch(params({ statusId: '7' })).statusId).toBe('7');
  });

  it.each(['0', '-1', '1.5', 'abc'])('reads an invalid statusId %s as empty', (raw) => {
    expect(readSearch(params({ statusId: raw })).statusId).toBe('');
  });

  it('reads an unrecognised type as empty', () => {
    expect(readSearch(params({ type: 'nonsense' })).type).toBe('');
  });

  it('reads a recognised type as itself', () => {
    expect(readSearch(params({ type: 'bug' })).type).toBe('bug');
  });

  it.each(['createdFrom', 'createdTo', 'closedFrom', 'closedTo'] as const)(
    'reads a well-formed %s as itself',
    (field) => {
      expect(readSearch(params({ [field]: '2026-03-05' }))[field]).toBe('2026-03-05');
    },
  );

  it('reads 2026-1-1 as empty, not a loosely-formed date', () => {
    expect(readSearch(params({ createdFrom: '2026-1-1' })).createdFrom).toBe('');
  });

  it('reads garbage as empty', () => {
    expect(readSearch(params({ createdFrom: 'garbage' })).createdFrom).toBe('');
  });
});

describe('writeSearch', () => {
  it('omits a blank q', () => {
    expect(writeSearch({ ...EMPTY_SEARCH, q: '' }).has('q')).toBe(false);
  });

  it('omits a whitespace-only q', () => {
    expect(writeSearch({ ...EMPTY_SEARCH, q: '   ' }).has('q')).toBe(false);
  });

  it('writes a full filter in the fixed order', () => {
    const full: SearchFilters = {
      q: 'needle',
      projectId: '1',
      type: 'bug',
      statusId: '2',
      createdFrom: '2026-01-01',
      createdTo: '2026-01-31',
      closedFrom: '2026-02-01',
      closedTo: '2026-02-28',
    };

    expect([...writeSearch(full).keys()]).toEqual([
      'q',
      'projectId',
      'type',
      'statusId',
      'createdFrom',
      'createdTo',
      'closedFrom',
      'closedTo',
    ]);
  });

  it('round-trips a valid filter through readSearch', () => {
    const full: SearchFilters = {
      q: 'needle',
      projectId: '1',
      type: 'bug',
      statusId: '2',
      createdFrom: '2026-01-01',
      createdTo: '2026-01-31',
      closedFrom: '2026-02-01',
      closedTo: '2026-02-28',
    };

    expect(readSearch(writeSearch(full))).toEqual(full);
  });
});

describe('searchRequest', () => {
  it('always writes limit=50', () => {
    expect(searchRequest(EMPTY_SEARCH, 0).get('limit')).toBe(String(SEARCH_PAGE));
  });

  it('omits offset 0', () => {
    expect(searchRequest(EMPTY_SEARCH, 0).has('offset')).toBe(false);
  });

  it('writes a non-zero offset', () => {
    expect(searchRequest(EMPTY_SEARCH, 50).get('offset')).toBe('50');
  });

  it('sends every scope id, in order, when no project is chosen', () => {
    expect(searchRequest(EMPTY_SEARCH, 0, [3, 7]).getAll('projectId')).toEqual(['3', '7']);
  });

  it('sends only the chosen project, not the scope', () => {
    const chosen = { ...EMPTY_SEARCH, projectId: '7' };
    expect(searchRequest(chosen, 0, [3, 7]).getAll('projectId')).toEqual(['7']);
  });

  it('sends no projectId for no scope and no project', () => {
    expect(searchRequest(EMPTY_SEARCH, 0).has('projectId')).toBe(false);
  });
});

describe('scrubbedProjectId', () => {
  const offered = [{ id: 1 }, { id: 2 }];

  it('keeps a project the facet offers', () => {
    expect(scrubbedProjectId('2', offered)).toBe('2');
  });

  it('scrubs an id for a project outside the scope', () => {
    expect(scrubbedProjectId('3', offered)).toBe('');
  });

  it('scrubs when the facet is hidden for one offered project', () => {
    expect(scrubbedProjectId('1', [{ id: 1 }])).toBe('');
  });

  it('is already empty and stays so', () => {
    expect(scrubbedProjectId('', offered)).toBe('');
  });
});

describe('isSearching', () => {
  it('is false for the empty filter', () => {
    expect(isSearching(EMPTY_SEARCH)).toBe(false);
  });

  it('is false for a whitespace-only q', () => {
    expect(isSearching({ ...EMPTY_SEARCH, q: '   ' })).toBe(false);
  });

  it('is true when any field is set', () => {
    expect(isSearching({ ...EMPTY_SEARCH, projectId: '1' })).toBe(true);
  });
});

describe('countLine', () => {
  it('reads "0 results" when nothing matched', () => {
    expect(countLine(0, 0)).toBe('0 results');
  });

  it('reads "N of total" otherwise', () => {
    expect(countLine(3, 340)).toBe('3 of 340');
  });
});

describe('hasMore', () => {
  it('is false when shown equals total', () => {
    expect(hasMore(50, 50)).toBe(false);
  });

  it('is true when shown is less than total', () => {
    expect(hasMore(50, 51)).toBe(true);
  });
});

describe('matchedWords', () => {
  it('joins multiple matches with a comma', () => {
    expect(matchedWords(['title', 'comment'])).toBe('title, comment');
  });

  it('words work-log as "work log"', () => {
    expect(matchedWords(['work-log'])).toBe('work log');
  });

  it('is empty for no matches', () => {
    expect(matchedWords([])).toBe('');
  });
});

describe('statusPath', () => {
  it('links to the search page filtered by status', () => {
    expect(statusPath(7)).toBe('/search?statusId=7');
  });
});

describe('searchPath', () => {
  it('is null for a blank query', () => {
    expect(searchPath('')).toBeNull();
  });

  it('is null for a whitespace-only query', () => {
    expect(searchPath('   ')).toBeNull();
  });

  it('links to the search page with the query', () => {
    expect(searchPath('ha 99')).toBe('/search?q=ha+99');
  });

  it('trims the query', () => {
    expect(searchPath(' sync bug ')).toBe('/search?q=sync+bug');
  });

  it('encodes a query with special characters rather than splitting it', () => {
    expect(searchPath('a&b=c')).toBe('/search?q=a%26b%3Dc');
  });
});
