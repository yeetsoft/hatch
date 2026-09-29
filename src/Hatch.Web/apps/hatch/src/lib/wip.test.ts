import { describe, expect, it } from 'vitest';
import { limitDraft, limitRequest, meterText, preview, runs, tightness, toggled, wipBlocked } from './wip';
import { boardColumns } from './columns';
import type { IssueCard, Status, Wip, WipSection } from '../types';

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

const TODO = status(1);
const DOING = status(2);
const REVIEW = status(3);
const DONE = status(4, { isTerminal: true });
const SHELVED = status(5, { isDeferred: true });

const STATUSES = [TODO, DOING, REVIEW, DONE, SHELVED];

const section = (over: Partial<WipSection> = {}): WipSection => ({
  limit: null,
  types: ['story', 'bug'],
  statusIds: [],
  ...over,
});

const wip = (over: Partial<Wip> = {}): Wip => ({
  limit: 5,
  types: ['story', 'bug'],
  statusIds: [],
  load: 3,
  claimedInbound: 0,
  ...over,
});

const card = (over: Partial<IssueCard> = {}): IssueCard => ({
  key: 'AER-1',
  projectKey: 'AER',
  type: 'story',
  title: 'A card',
  statusId: TODO.id,
  rank: 0,
  parentKey: null,
  readyAt: null,
  dueAt: null,
  openQuestions: 0,
  assignee: null,
  claim: null,
  expedited: false,
  express: false,
  ...over,
});

describe('wipBlocked', () => {
  it('is null for an ordinary column', () => {
    expect(wipBlocked(TODO)).toBeNull();
  });

  it('names why a deferred column is blocked', () => {
    expect(wipBlocked(SHELVED)).toContain('parked work');
  });

  it('names why a done column is blocked', () => {
    expect(wipBlocked(DONE)).toContain('shipped work');
  });
});

describe('toggled', () => {
  it('adds a column, in board order', () => {
    const held = section({ statusIds: [DOING.id] });

    expect(toggled(held, STATUSES, REVIEW.id, true)).toEqual([DOING.id, REVIEW.id]);
  });

  it('adds a column ahead of ones already held, in board order rather than append order', () => {
    const held = section({ statusIds: [REVIEW.id] });

    expect(toggled(held, STATUSES, DOING.id, true)).toEqual([DOING.id, REVIEW.id]);
  });

  it('removes a column', () => {
    const held = section({ statusIds: [DOING.id, REVIEW.id] });

    expect(toggled(held, STATUSES, DOING.id, false)).toEqual([REVIEW.id]);
  });

  it('unticking a column not held changes nothing', () => {
    const held = section({ statusIds: [DOING.id] });

    expect(toggled(held, STATUSES, REVIEW.id, false)).toEqual([DOING.id]);
  });
});

describe('the limit field', () => {
  it('shows no limit as a blank box', () => {
    expect(limitDraft(section({ limit: null }))).toBe('');
  });

  it('shows a held limit as its number', () => {
    expect(limitDraft(section({ limit: 5 }))).toBe('5');
  });

  it('trims the field for the wire', () => {
    expect(limitRequest(' 5 ')).toBe('5');
  });

  it('a blank field clears the limit', () => {
    expect(limitRequest('  ')).toBe('');
  });
});

describe('runs', () => {
  it('is one run for adjacent WIP columns', () => {
    expect(runs([TODO, DOING, REVIEW, DONE], wip({ statusIds: [DOING.id, REVIEW.id] }))).toEqual([
      { start: 1, statusIds: [DOING.id, REVIEW.id] },
    ]);
  });

  it('splits into two runs around a column outside the section', () => {
    const BLOCKED = status(6);
    expect(runs([TODO, DOING, BLOCKED, REVIEW, DONE], wip({ statusIds: [DOING.id, REVIEW.id] }))).toEqual([
      { start: 1, statusIds: [DOING.id] },
      { start: 3, statusIds: [REVIEW.id] },
    ]);
  });

  it('is not split by a deferred column that boardColumns has already dropped', () => {
    const drawn = boardColumns([TODO, DOING, SHELVED, REVIEW, DONE]);
    expect(runs(drawn, wip({ statusIds: [DOING.id, REVIEW.id] }))).toEqual([
      { start: 1, statusIds: [DOING.id, REVIEW.id] },
    ]);
  });

  it('ignores a WIP id that is not among the drawn columns', () => {
    expect(runs([TODO, DOING], wip({ statusIds: [DOING.id, REVIEW.id] }))).toEqual([{ start: 1, statusIds: [DOING.id] }]);
  });

  it('is empty for no section', () => {
    expect(runs([TODO, DOING], null)).toEqual([]);
  });
});

describe('tightness', () => {
  it('is room with two or more left', () => {
    expect(tightness(3, 5)).toBe('room');
  });

  it('is tight with one left', () => {
    expect(tightness(4, 5)).toBe('tight');
  });

  it('is full at the limit', () => {
    expect(tightness(5, 5)).toBe('full');
  });

  it('is over above the limit', () => {
    expect(tightness(6, 5)).toBe('over');
  });

  it('is tight with a limit of one and nothing in it yet', () => {
    expect(tightness(0, 1)).toBe('tight');
  });

  it('is room with a limit of two and nothing in it yet', () => {
    expect(tightness(0, 2)).toBe('room');
  });
});

describe('preview', () => {
  it('adds one for a story from outside the section', () => {
    expect(preview(wip({ load: 3, types: ['story', 'bug'], statusIds: [DOING.id] }), card({ type: 'story', statusId: TODO.id }))).toBe(4);
  });

  it('adds one for a bug from outside the section', () => {
    expect(preview(wip({ load: 3, types: ['story', 'bug'], statusIds: [DOING.id] }), card({ type: 'bug', statusId: TODO.id }))).toBe(4);
  });

  it('changes nothing for a task', () => {
    expect(preview(wip({ load: 3, types: ['story', 'bug'], statusIds: [DOING.id] }), card({ type: 'task', statusId: TODO.id }))).toBe(3);
  });

  it('changes nothing for an epic', () => {
    expect(preview(wip({ load: 3, types: ['story', 'bug'], statusIds: [DOING.id] }), card({ type: 'epic', statusId: TODO.id }))).toBe(3);
  });

  it('changes nothing for a story already inside the section', () => {
    expect(preview(wip({ load: 3, types: ['story', 'bug'], statusIds: [DOING.id] }), card({ type: 'story', statusId: DOING.id }))).toBe(3);
  });

  it('changes nothing with no card', () => {
    expect(preview(wip({ load: 3 }), null)).toBe(3);
  });
});

describe('meterText', () => {
  it('reads the load against the limit', () => {
    expect(meterText(wip({ limit: 5 }), 3)).toBe('WIP 3 of 5');
  });

  it('names full at the limit', () => {
    expect(meterText(wip({ limit: 5 }), 5)).toBe('WIP 5 of 5 · full');
  });

  it('names over above the limit', () => {
    expect(meterText(wip({ limit: 5 }), 6)).toBe('WIP 6 of 5 · over');
  });

  it('says what is on the way in', () => {
    expect(meterText(wip({ limit: 5, claimedInbound: 1 }), 3)).toBe('WIP 3 of 5 (1 on the way)');
  });

  it('puts what is on the way in before the over wording', () => {
    expect(meterText(wip({ limit: 5, claimedInbound: 1 }), 6)).toBe('WIP 6 of 5 (1 on the way) · over');
  });
});
