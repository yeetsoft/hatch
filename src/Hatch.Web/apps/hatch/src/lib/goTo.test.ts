import { describe, expect, it } from 'vitest';
import { GO_TO_LIMIT, goToRows, goToStatus, keepHighlight, whereOnBoard } from './goTo';
import type { IssueCard, Status } from '../types';

/* Spread onto rather than written whole, so a new field on IssueCard is one
   edit here and not one per case - the same shape issuePicker.test.ts uses. */
const card = (key: string, over: Partial<IssueCard> = {}): IssueCard => ({
  key,
  projectKey: key.slice(0, key.lastIndexOf('-')),
  type: 'task',
  title: 'A title',
  statusId: 1,
  rank: 1024,
  parentKey: null,
  readyAt: null,
  dueAt: null,
  openQuestions: 0,
  assignee: null,
  claim: null,
  expedited: false,
  priority: 'normal',
  express: false,
  ...over,
});

const status = (id: number, over: Partial<Status> = {}): Status => ({
  id,
  name: `Column ${id}`,
  sortOrder: id,
  isTerminal: false,
  isDeferred: false,
  isWip: false,
  expressSkips: false,
  color: '#336699',
  ...over,
});

const numbered = (project: string, ...indexes: number[]) => indexes.map((n) => card(`${project}-${n}`));
const keys = (cards: IssueCard[], query: string) => goToRows(cards, query).rows.map((r) => r.card.key);

describe('goToRows: numbers', () => {
  const board = numbered('AER', 1, 2, 10, 11, 12, 19, 20, 100, 110, 111, 211, 13);

  it('lists every issue whose number begins with the digits, lowest first', () => {
    expect(keys(board, '1')).toEqual(['AER-1', 'AER-10', 'AER-11', 'AER-12', 'AER-13', 'AER-19', 'AER-100', 'AER-110', 'AER-111']);
  });

  it('does not list 211 for 11', () => {
    expect(keys(board, '11')).toEqual(['AER-11', 'AER-110', 'AER-111']);
  });

  it('reads leading zeros as no number at all', () => {
    expect(keys(board, '01')).toEqual([]);
  });

  it('puts every project`s issue 1 before any issue 10', () => {
    const two = [...numbered('AER', 1, 10), ...numbered('BQX', 1, 10)];
    expect(keys(two, '1')).toEqual(['AER-1', 'BQX-1', 'AER-10', 'BQX-10']);
  });

  it('is a rule on the whole query, so a project key with digits is not a number', () => {
    const board = [card('A1B-5'), card('AER-1')];
    expect(keys(board, '1')).toEqual(['AER-1']);
  });
});

describe('goToRows: keys', () => {
  const board = numbered('AER', 3, 13, 30, 31, 300);

  it('lists the keys that begin with what was typed, and not AER-13', () => {
    for (const query of ['AER-3', 'aer-3', 'aer3']) {
      expect(keys(board, query)).toEqual(['AER-3', 'AER-30', 'AER-31', 'AER-300']);
    }
  });

  it('takes the project half alone', () => {
    expect(keys([...numbered('AER', 1), ...numbered('BQX', 1)], 'bq')).toEqual(['BQX-1']);
  });

  it('gives a query with nothing but punctuation no key match', () => {
    expect(keys(board, '-')).toEqual([]);
  });

  it('gives an empty or blank query no rows', () => {
    expect(goToRows(board, '')).toEqual({ rows: [], total: 0 });
    expect(goToRows(board, '   ')).toEqual({ rows: [], total: 0 });
  });

  it('marks the typed characters, across the hyphen', () => {
    const [row] = goToRows([card('AER-30')], 'aer3').rows;
    expect(row.field).toBe('key');
    expect(row.marks).toEqual([{ start: 0, end: 5 }]);
    expect(goToRows([card('AER-30')], 'aer-3').rows[0].marks).toEqual([{ start: 0, end: 5 }]);
    expect(goToRows([card('AER-30')], 'ae').rows[0].marks).toEqual([{ start: 0, end: 2 }]);
  });

  it('marks the digits of a number match on the number', () => {
    expect(goToRows([card('AER-110')], '11').rows[0].marks).toEqual([{ start: 4, end: 6 }]);
  });
});

describe('goToRows: titles', () => {
  it('finds titles that hold every word, in any order, sorted by key', () => {
    const board = [
      card('AER-9', { title: 'Peek: the description box scrolls' }),
      card('AER-2', { title: 'Board: filters live in the URL' }),
      card('AER-4', { title: 'The description of a filter' }),
    ];
    expect(keys(board, 'description peek')).toEqual(['AER-9']);
    expect(keys(board, 'the description')).toEqual(['AER-4', 'AER-9']);
  });

  it('marks every occurrence of every word, merged where they touch', () => {
    const [row] = goToRows([card('AER-1', { title: 'Board board: filters' })], 'board filters').rows;
    expect(row.field).toBe('title');
    expect(row.marks).toEqual([
      { start: 0, end: 5 },
      { start: 6, end: 11 },
      { start: 13, end: 20 },
    ]);
  });

  it('lists a key match before a title match', () => {
    const board = [card('AER-2', { title: 'Mentions 1 in passing' }), card('AER-10', { title: 'Nothing' })];
    expect(keys(board, '1')).toEqual(['AER-10', 'AER-2']);
  });

  it('lists an issue once, however it matched', () => {
    const board = [card('AER-1', { title: 'Fix AER-1 regression' })];
    const result = goToRows(board, 'aer-1');
    expect(result.rows).toHaveLength(1);
    expect(result.rows[0].field).toBe('key');
    expect(result.total).toBe(1);
  });

  it('lets a several-word query match titles only', () => {
    expect(keys([card('AER-1', { title: 'one two' })], 'aer one')).toEqual([]);
  });
});

describe('goToRows: the limit', () => {
  const many = Array.from({ length: GO_TO_LIMIT + 12 }, (_, i) => card(`AER-${i + 1}`));

  it('draws at most the limit and counts the rest', () => {
    const result = goToRows(many, 'aer');
    expect(result.rows).toHaveLength(GO_TO_LIMIT);
    expect(result.total).toBe(many.length);
  });

  it('does not mutate what it was handed', () => {
    const before = many.map((c) => c.key);
    goToRows(many, 'aer');
    expect(many.map((c) => c.key)).toEqual(before);
  });
});

describe('keepHighlight', () => {
  const rows = goToRows(numbered('AER', 1, 2, 3), 'aer').rows;

  it('keeps the same key while it is still listed', () => {
    expect(keepHighlight(rows, 'AER-2')).toBe('AER-2');
  });

  it('falls to the first row when its issue has gone, or nothing was highlighted', () => {
    expect(keepHighlight(rows, 'AER-9')).toBe('AER-1');
    expect(keepHighlight(rows, null)).toBe('AER-1');
  });

  it('is null when there are no rows', () => {
    expect(keepHighlight([], 'AER-1')).toBeNull();
  });
});

describe('goToStatus', () => {
  it('says what can be typed for an empty prompt', () => {
    expect(goToStatus('  ', 0, 0)).toBe('type a key, a number, or words from a title');
  });

  it('counts, and pluralises', () => {
    expect(goToStatus('aer', 1, 1)).toBe('1 issue');
    expect(goToStatus('aer', 4, 4)).toBe('4 issues');
  });

  it('says so when the list is cut', () => {
    expect(goToStatus('1', 50, 112)).toBe('50 of 112 — keep typing');
  });

  it('says so when nothing matches', () => {
    expect(goToStatus('zzz', 0, 0)).toBe('no issue matches');
  });
});

describe('whereOnBoard', () => {
  const columns = [status(1), status(2)];
  const visible = new Set(['AER-1']);

  it('is drawn when the card is in a drawn column and the filter lets it through', () => {
    expect(whereOnBoard(card('AER-1'), visible, columns)).toBe('drawn');
  });

  it('is filtered when the filter is hiding it', () => {
    expect(whereOnBoard(card('AER-2'), visible, columns)).toBe('filtered');
  });

  it('is undrawn in a column the board does not draw, even when the filter hides it too', () => {
    expect(whereOnBoard(card('AER-1', { statusId: 9 }), visible, columns)).toBe('undrawn');
    expect(whereOnBoard(card('AER-2', { statusId: 9 }), visible, columns)).toBe('undrawn');
  });

  it('is drawn for a card folded behind waiting, because the fold will open', () => {
    const waiting = card('AER-1', { readyAt: '2999-01-01' });
    expect(whereOnBoard(waiting, visible, columns)).toBe('drawn');
  });
});
