import { describe, expect, it } from 'vitest';
import { GO_TO_LIMIT } from './goTo';
import { listEntries, listedKeys, mergeAnswer, narrows, needsLookup, scopeToken } from './goToKeys';
import type { GoToAnswered, GoToEntry, GoToListing } from './goToKeys';
import type { GoToRowDto, IssueCard, Project } from '../types';

/* Only what the listing reads; the rest of a Project is not its business. */
const project = (id: number, key: string, lastIssueNumber: number): Project =>
  ({ id, key, name: key, lastIssueNumber }) as Project;

const card = (key: string, title = 'A title'): IssueCard =>
  ({ key, projectKey: key.slice(0, key.lastIndexOf('-')), type: 'task', title, statusId: 1 }) as IssueCard;

const row = (key: string, over: Partial<GoToRowDto> = {}): GoToRowDto => {
  const hyphen = key.lastIndexOf('-');
  return {
    key,
    projectKey: key.slice(0, hyphen),
    number: Number(key.slice(hyphen + 1)),
    type: 'task',
    title: `Title of ${key}`,
    outOfScope: false,
    statusId: 2,
    statusName: 'Done',
    statusColor: '#336699',
    ...over,
  };
};

const list = (
  query: string,
  projects: Project[],
  cards: IssueCard[] = [],
  scopeKeys: string[] = [],
  allProjects: Project[] = projects,
): GoToListing => listEntries({ cards, projects, allProjects, scopeKeys, query });

const keysOf = (listing: GoToListing) => listing.entries.map((entry) => entry.key);

const answered = (query: string, rows: string[], more = false, scope = ''): GoToAnswered => ({
  query,
  scope,
  more,
  rows: rows.map((key) => row(key)),
});

describe('listedKeys', () => {
  const projects = [project(1, 'AER', 1000), project(2, 'BEN', 300), project(3, 'CAL', 5000)];

  it('lists 455 in every project that got that far, and counts what the numbers name', () => {
    const listing = listedKeys(projects, projects, '455');

    // AER-455 and CAL-455, then CAL's 4550 to 4559. BEN stopped at 300.
    expect(listing.keys.map((k) => k.key).slice(0, 3)).toEqual(['AER-455', 'CAL-455', 'CAL-4550']);
    expect(listing.keys.map((k) => k.key)).not.toContain('BEN-455');
    expect(listing.total).toBe(1 + 1 + 10);
    expect(listing.has('CAL', 4559)).toBe(true);
    expect(listing.has('BEN', 455)).toBe(false);
  });

  it('puts every project’s issue 4 before any issue 40', () => {
    const keys = listedKeys(projects, projects, '4').keys.map((k) => k.key);

    expect(keys.slice(0, 4)).toEqual(['AER-4', 'BEN-4', 'CAL-4', 'AER-40']);
  });

  it('counts a short number over a huge project without walking it', () => {
    const big = [project(1, 'AER', 2_000_000)];
    const listing = listedKeys(big, big, '4');

    expect(listing.total).toBe(1 + 10 + 100 + 1000 + 10000 + 100000);
    expect(listing.keys).toHaveLength(GO_TO_LIMIT);
  });

  it('stops at the project’s last number', () => {
    const one = [project(1, 'AER', 47)];

    expect(listedKeys(one, one, '4').keys.map((k) => k.number)).toEqual([4, 40, 41, 42, 43, 44, 45, 46, 47]);
    expect(listedKeys(one, one, '48').total).toBe(0);
    expect(listedKeys(one, one, '0').total).toBe(0);
  });

  it('reads a key prefix, with the hyphen or without', () => {
    const one = [project(1, 'HA', 40), project(2, 'HA3', 12)];

    expect(listedKeys(one, one, 'ha-3').keys.map((k) => k.key)).toEqual(listedKeys(one, one, 'HA3').keys.map((k) => k.key));
    // Every HA3 issue, and HA's 3 and 30 to 39.
    expect(listedKeys(one, one, 'ha3').total).toBe(12 + 11);
    // HA-31, and HA3's 1, 10, 11 and 12.
    expect(listedKeys(one, one, 'ha31').keys.map((k) => k.key)).toEqual(['HA3-1', 'HA3-10', 'HA3-11', 'HA3-12', 'HA-31']);
    expect(listedKeys(one, one, 'ha').total).toBe(52);
  });

  it('lists a whole key outside the scope first, and nothing else outside it', () => {
    const all = [project(1, 'HA', 40), project(2, 'FIL', 12)];
    const inScope = [all[0]];

    const whole = listedKeys(inScope, all, 'fil-1');
    expect(whole.keys).toEqual([{ key: 'FIL-1', projectKey: 'FIL', number: 1, outOfScope: true }]);

    // `fil-1` is FIL-1 and not FIL-10, and a prefix is not a whole key.
    expect(listedKeys(inScope, all, 'fil').total).toBe(0);
    expect(listedKeys(inScope, all, 'fil-13').total).toBe(0);
    expect(listedKeys(inScope, all, '1').keys.every((k) => k.projectKey === 'HA')).toBe(true);
  });

  it('names no key for several words, or for nothing', () => {
    expect(listedKeys(projects, projects, 'a title').total).toBe(0);
    expect(listedKeys(projects, projects, '  ').total).toBe(0);
  });

  it('skips a project that has handed out nothing', () => {
    const empty = [project(1, 'AER', 0)];
    expect(listedKeys(empty, empty, 'aer').total).toBe(0);
  });
});

describe('listEntries', () => {
  const projects = [project(1, 'AER', 1000), project(2, 'BEN', 300), project(3, 'CAL', 5000)];

  it('lists 455 across three projects with the one that fell short left out', () => {
    const { entries, total } = list('455', projects);

    expect(keysOf({ entries, total })[0]).toBe('AER-455');
    expect(keysOf({ entries, total })).not.toContain('BEN-455');
    expect(total).toBe(12);
    expect(entries.every((entry) => !entry.card && !entry.looked)).toBe(true);
    // The three digits are lit in the number, after the hyphen.
    expect(entries[0].marks).toEqual([{ start: 4, end: 7 }]);
  });

  it('titles a key whose card the page holds, and leaves the others to wait', () => {
    const held = card('CAL-455', 'Held already');
    const { entries } = list('455', projects, [held]);

    expect(entries.find((entry) => entry.key === 'CAL-455')?.card).toBe(held);
    expect(entries.find((entry) => entry.key === 'AER-455')?.card).toBeUndefined();
  });

  it('caps at fifty and says how many there are', () => {
    const { entries, total } = list('4', projects);

    expect(entries).toHaveLength(GO_TO_LIMIT);
    // AER 111, BEN 11 (stopped at 300), CAL 1111: the numbers say how many.
    expect(total).toBe(111 + 11 + 1111);
  });

  it('adds the issues whose title holds the words after the keys, without repeating one', () => {
    const cards = [card('AER-7', 'Fix 455 rendering'), card('AER-455', 'Fix 455 rendering')];
    const { entries, total } = list('455', projects, cards);

    const keys = keysOf({ entries, total });
    expect(keys.filter((key) => key === 'AER-455')).toHaveLength(1);
    expect(keys.indexOf('AER-7')).toBeGreaterThan(keys.indexOf('AER-455'));
    expect(entries.find((entry) => entry.key === 'AER-7')?.field).toBe('title');
    expect(total).toBe(12 + 1);
  });

  it('finds title words alone when no key is named', () => {
    const { entries } = list('login bug', projects, [card('AER-7', 'Login bug on mobile'), card('AER-8', 'Other')]);

    expect(keysOf({ entries, total: 0 })).toEqual(['AER-7']);
  });

  it('lists a card past a stale last number, so the page loses nothing it can see', () => {
    const stale = [project(1, 'AER', 3)];
    const { entries, total } = list('7', stale, [card('AER-7', 'Seen already')]);

    expect(keysOf({ entries, total })).toEqual(['AER-7']);
    expect(total).toBe(1);
    expect(entries[0].card).toBeDefined();
  });

  it('puts a whole key outside the scope first', () => {
    const all = [project(1, 'HA', 40), project(2, 'FIL', 12)];
    const { entries } = list('fil-1', [all[0]], [], ['HA'], all);

    expect(entries).toHaveLength(1);
    expect(entries[0]).toMatchObject({ key: 'FIL-1', outOfScope: true });
  });

  it('lists nothing for a blank query', () => {
    expect(list('  ', projects)).toEqual({ entries: [], total: 0 });
  });
});

describe('mergeAnswer', () => {
  const projects = [project(1, 'HA', 12)];

  it('lets the answer title a key, and a card win over it', () => {
    const held = card('HA-10', 'In hand');
    const merged = mergeAnswer(list('1', projects, [held]), answered('1', ['HA-1', 'HA-10', 'HA-11', 'HA-12']), '1', '');

    expect(merged.entries.find((e) => e.key === 'HA-1')?.looked?.title).toBe('Title of HA-1');
    const ten = merged.entries.find((e) => e.key === 'HA-10')!;
    expect(ten.card).toBe(held);
    expect(ten.looked).toBeDefined();
  });

  it('drops a deleted key once a complete answer lacks it', () => {
    const merged = mergeAnswer(list('1', projects), answered('1', ['HA-1', 'HA-10', 'HA-12']), '1', '');

    expect(keysOf(merged)).toEqual(['HA-1', 'HA-10', 'HA-12']);
    expect(merged.total).toBe(3);
  });

  it('keeps a key past the end of an incomplete answer, and drops one inside it', () => {
    const merged = mergeAnswer(list('1', projects), answered('1', ['HA-1', 'HA-12'], true), '1', '');

    // HA-10 and HA-11 sit before the last row and are not in it; none sits after.
    expect(keysOf(merged)).toEqual(['HA-1', 'HA-12']);

    const cut = mergeAnswer(list('1', projects), answered('1', ['HA-1', 'HA-10'], true), '1', '');
    expect(keysOf(cut)).toEqual(['HA-1', 'HA-10', 'HA-11', 'HA-12']);
    expect(cut.total).toBe(4);
  });

  it('never drops a card the page holds', () => {
    const merged = mergeAnswer(list('1', projects, [card('HA-11')]), answered('1', ['HA-1']), '1', '');

    expect(keysOf(merged)).toContain('HA-11');
  });

  it('applies a complete answer to a query that only narrows it', () => {
    const wide = [project(1, 'HA', 500)];
    const reply = answered('4', ['HA-4', 'HA-40', 'HA-41', 'HA-45', 'HA-400', 'HA-450']);
    const merged = mergeAnswer(list('45', wide), reply, '45', '');

    // HA-45 and HA-450 exist; the other numbers starting 45 were deleted.
    expect(keysOf(merged)).toEqual(['HA-45', 'HA-450']);
    expect(merged.total).toBe(2);
  });

  it('does not drop for a query that is not narrowed by the answer’s', () => {
    const wide = [project(1, 'HA', 500)];
    const merged = mergeAnswer(list('5', wide), answered('4', ['HA-4']), '5', '');

    expect(merged.entries.some((e) => e.key === 'HA-5')).toBe(true);
    expect(merged.entries.every((e) => !e.looked)).toBe(true);
  });

  it('adds an answered key the numbers did not name, once', () => {
    const stale = [project(1, 'HA', 3)];
    const merged = mergeAnswer(list('9', stale), answered('9', ['HA-9']), '9', '');

    expect(keysOf(merged)).toEqual(['HA-9']);
    expect(merged.total).toBe(1);
    expect(mergeAnswer(merged, answered('9', ['HA-9']), '9', '').total).toBe(1);
  });

  it('does not add an old answer’s rows to a query they do not match', () => {
    const merged = mergeAnswer(list('5', projects), answered('4', ['HA-400']), '5', '');

    expect(keysOf(merged)).not.toContain('HA-400');
  });

  it('does not list an out-of-scope row for a prefix, only for its whole key', () => {
    const reply: GoToAnswered = { query: 'fil-1', scope: '1', more: false, rows: [row('FIL-1', { outOfScope: true })] };
    const mine = [project(1, 'HA', 5)];

    expect(keysOf(mergeAnswer(list('fil', mine), reply, 'fil', '1'))).toEqual([]);
    expect(keysOf(mergeAnswer(list('fil-1', mine), reply, 'fil-1', '1'))).toEqual(['FIL-1']);
  });

  it('draws the row’s own column, kept on the entry', () => {
    const merged = mergeAnswer(list('1', projects), answered('1', ['HA-1']), '1', '');
    const looked = merged.entries.find((e) => e.key === 'HA-1')!.looked!;

    expect(looked).toMatchObject({ statusName: 'Done', statusColor: '#336699' });
  });

  it('ignores an answer to another scope', () => {
    const base = list('1', projects);

    expect(mergeAnswer(base, answered('1', ['HA-1'], false, '1,2'), '1', '')).toBe(base);
  });
});

describe('needsLookup', () => {
  const projects = [project(1, 'HA', 500)];
  const entries = (query: string, cards: IssueCard[] = []): GoToEntry[] => list(query, projects, cards).entries;

  it('asks for a key the page holds no card for', () => {
    expect(needsLookup(entries('455'), '455', '', null)).toBe(true);
  });

  it('sends nothing when every row to be drawn already has a card', () => {
    const cards = [card('HA-4'), ...[0, 1, 2, 3, 4, 5, 6, 7, 8, 9].map((n) => card(`HA-4${n}`))];
    const rows = entries('4', cards).slice(0, 11);

    expect(rows.every((entry) => entry.card)).toBe(true);
    expect(needsLookup(rows, '4', '', null)).toBe(false);
  });

  it('sends nothing for a query that names no key', () => {
    expect(needsLookup(entries('login bug', [card('HA-1', 'Login bug')]), 'login bug', '', null)).toBe(false);
    expect(needsLookup([], '4', '', null)).toBe(false);
  });

  it('sends nothing for a narrowing query after a complete answer, and asks after an incomplete one', () => {
    const complete = { query: '4', scope: '', more: false };

    expect(needsLookup(entries('45'), '45', '', complete)).toBe(false);
    expect(needsLookup(entries('45'), '45', '', { ...complete, more: true })).toBe(true);
  });

  it('asks again when the query is not narrowed, or the scope has changed', () => {
    const complete = { query: '4', scope: '', more: false };

    expect(needsLookup(entries('5'), '5', '', complete)).toBe(true);
    expect(needsLookup(entries('45'), '45', '1', complete)).toBe(true);
  });

  it('sends nothing for a query that was just answered, complete or not', () => {
    expect(needsLookup(entries('4'), '4', '', { query: '4', scope: '', more: true })).toBe(false);
  });
});

describe('narrows', () => {
  it('is true for a longer number of the same digits, or a longer word', () => {
    expect(narrows('4', '45')).toBe(true);
    expect(narrows('4', '4')).toBe(true);
    expect(narrows('ha', 'ha-3')).toBe(true);
    expect(narrows('ha-3', 'ha31')).toBe(true);
  });

  it('is false across kinds, for other starts, and for several words', () => {
    expect(narrows('45', '4')).toBe(false);
    expect(narrows('4', 'ha4')).toBe(false);
    expect(narrows('ha4', 'ha')).toBe(false);
    expect(narrows('a b', 'a b c')).toBe(false);
    expect(narrows('', '4')).toBe(false);
  });
});

describe('scopeToken', () => {
  it('is the same for the same projects in any order', () => {
    expect(scopeToken([3, 1, 2])).toBe('1,2,3');
    expect(scopeToken([])).toBe('');
  });
});
