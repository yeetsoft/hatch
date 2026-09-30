import { describe, expect, it } from 'vitest';
import { limitDraft, limitRequest, meterText, plural, preview, runs, tightest, tightness, toggled, wipBlocked } from './wip';
import { boardColumns } from './columns';
import type { IssueCard, Status, Wip, WipSection, WipSlice, WipSliceSetting } from '../types';

const status = (id: number, over: Partial<Status> = {}): Status => ({
  id,
  name: `Column ${id}`,
  sortOrder: id,
  isTerminal: false,
  isDeferred: false,
  isWip: false,
  expressSkips: false,
  parentPulls: false,
  color: '#336699',
  ...over,
});

const TODO = status(1);
const DOING = status(2);
const REVIEW = status(3);
const DONE = status(4, { isTerminal: true });
const SHELVED = status(5, { isDeferred: true });

const STATUSES = [TODO, DOING, REVIEW, DONE, SHELVED];

const sliceSetting = (over: Partial<WipSliceSetting> = {}): WipSliceSetting => ({
  types: ['story', 'bug'],
  limit: null,
  ...over,
});

const section = (over: Partial<WipSection> = {}): WipSection => ({
  statusIds: [],
  slices: [sliceSetting(), sliceSetting({ types: ['epic'] })],
  ...over,
});

const wipSlice = (over: Partial<WipSlice> = {}): WipSlice => ({
  types: ['story', 'bug'],
  limit: 5,
  load: 3,
  claimedInbound: 0,
  ...over,
});

const wip = (over: Partial<Wip> = {}): Wip => ({
  statusIds: [],
  slices: [wipSlice(), wipSlice({ types: ['epic'], limit: null, load: 0 })],
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
  priority: 'normal',
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
    expect(limitDraft(sliceSetting({ limit: null }))).toBe('');
  });

  it('shows a held limit as its number', () => {
    expect(limitDraft(sliceSetting({ limit: 5 }))).toBe('5');
  });

  it('trims the field for the wire', () => {
    expect(limitRequest(' 5 ')).toBe('5');
  });

  it('a blank field clears the limit', () => {
    expect(limitRequest('  ')).toBe('');
  });
});

describe('plural', () => {
  it('reads a single type as its plural', () => {
    expect(plural(['story'])).toBe('stories');
  });

  it('joins two types with and', () => {
    expect(plural(['story', 'bug'])).toBe('stories and bugs');
  });

  it('reads epic as epics', () => {
    expect(plural(['epic'])).toBe('epics');
  });

  it('joins three or more with an Oxford comma', () => {
    expect(plural(['story', 'bug', 'task'])).toBe('stories, bugs, and tasks');
  });
});

describe('runs', () => {
  it('is one run for adjacent WIP columns', () => {
    expect(
      runs([TODO, DOING, REVIEW, DONE], wip({ statusIds: [DOING.id, REVIEW.id], slices: [wipSlice()] })),
    ).toEqual([{ start: 1, statusIds: [DOING.id, REVIEW.id] }]);
  });

  it('splits into two runs around a column outside the section', () => {
    const BLOCKED = status(6);
    expect(
      runs(
        [TODO, DOING, BLOCKED, REVIEW, DONE],
        wip({ statusIds: [DOING.id, REVIEW.id], slices: [wipSlice()] }),
      ),
    ).toEqual([
      { start: 1, statusIds: [DOING.id] },
      { start: 3, statusIds: [REVIEW.id] },
    ]);
  });

  it('is not split by a deferred column that boardColumns has already dropped', () => {
    const drawn = boardColumns([TODO, DOING, SHELVED, REVIEW, DONE]);
    expect(runs(drawn, wip({ statusIds: [DOING.id, REVIEW.id], slices: [wipSlice()] }))).toEqual([
      { start: 1, statusIds: [DOING.id, REVIEW.id] },
    ]);
  });

  it('ignores a WIP id that is not among the drawn columns', () => {
    expect(
      runs([TODO, DOING], wip({ statusIds: [DOING.id, REVIEW.id], slices: [wipSlice()] })),
    ).toEqual([{ start: 1, statusIds: [DOING.id] }]);
  });

  it('is empty for no section', () => {
    expect(runs([TODO, DOING], null)).toEqual([]);
  });

  it('is empty when no slice has a limit', () => {
    expect(
      runs([TODO, DOING], wip({ statusIds: [DOING.id], slices: [wipSlice({ limit: null }), wipSlice({ limit: null })] })),
    ).toEqual([]);
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

describe('tightest', () => {
  it('is null when no slice has a limit', () => {
    const w = wip({ slices: [wipSlice({ limit: null }), wipSlice({ types: ['epic'], limit: null })] });
    expect(tightest(w, [0, 0])).toBeNull();
  });

  it('reads the one limited slice when the other has none', () => {
    const w = wip({ slices: [wipSlice({ limit: 5 }), wipSlice({ types: ['epic'], limit: null })] });
    expect(tightest(w, [5, 0])).toBe('full');
  });

  it('is the tighter of two limited slices', () => {
    const w = wip({ slices: [wipSlice({ limit: 5 }), wipSlice({ types: ['epic'], limit: 2 })] });
    expect(tightest(w, [3, 2])).toBe('full');
  });

  it('is not loosened by a roomier slice', () => {
    const w = wip({ slices: [wipSlice({ limit: 5 }), wipSlice({ types: ['epic'], limit: 2 })] });
    expect(tightest(w, [3, 0])).toBe('room');
  });
});

describe('preview', () => {
  it('adds one to the story-and-bug slice for a story from outside the section', () => {
    const w = wip({ statusIds: [DOING.id], slices: [wipSlice({ load: 3 }), wipSlice({ types: ['epic'], limit: null, load: 0 })] });
    expect(preview(w, card({ type: 'story', statusId: TODO.id }))).toEqual([4, 0]);
  });

  it('adds one to the story-and-bug slice for a bug from outside the section', () => {
    const w = wip({ statusIds: [DOING.id], slices: [wipSlice({ load: 3 }), wipSlice({ types: ['epic'], limit: null, load: 0 })] });
    expect(preview(w, card({ type: 'bug', statusId: TODO.id }))).toEqual([4, 0]);
  });

  it('adds one to the epic slice alone for an epic from outside the section', () => {
    const w = wip({ statusIds: [DOING.id], slices: [wipSlice({ load: 3 }), wipSlice({ types: ['epic'], limit: 2, load: 1 })] });
    expect(preview(w, card({ type: 'epic', statusId: TODO.id }))).toEqual([3, 2]);
  });

  it('changes nothing for a task', () => {
    const w = wip({ statusIds: [DOING.id], slices: [wipSlice({ load: 3 }), wipSlice({ types: ['epic'], limit: 2, load: 1 })] });
    expect(preview(w, card({ type: 'task', statusId: TODO.id }))).toEqual([3, 1]);
  });

  it('changes nothing for a card already inside the section', () => {
    const w = wip({ statusIds: [DOING.id], slices: [wipSlice({ load: 3 }), wipSlice({ types: ['epic'], limit: 2, load: 1 })] });
    expect(preview(w, card({ type: 'story', statusId: DOING.id }))).toEqual([3, 1]);
  });

  it('changes nothing with no card', () => {
    const w = wip({ slices: [wipSlice({ load: 3 }), wipSlice({ types: ['epic'], limit: 2, load: 1 })] });
    expect(preview(w, null)).toEqual([3, 1]);
  });
});

describe('meterText', () => {
  it('reads the load against the limit', () => {
    const w = wip({ slices: [wipSlice({ limit: 5 }), wipSlice({ types: ['epic'], limit: null })] });
    expect(meterText(w, [3, 0])).toBe('WIP 3 of 5');
  });

  it('names full at the limit', () => {
    const w = wip({ slices: [wipSlice({ limit: 5 }), wipSlice({ types: ['epic'], limit: null })] });
    expect(meterText(w, [5, 0])).toBe('WIP 5 of 5 · full');
  });

  it('names over above the limit', () => {
    const w = wip({ slices: [wipSlice({ limit: 5 }), wipSlice({ types: ['epic'], limit: null })] });
    expect(meterText(w, [6, 0])).toBe('WIP 6 of 5 · over');
  });

  it('says what is on the way in', () => {
    const w = wip({ slices: [wipSlice({ limit: 5, claimedInbound: 1 }), wipSlice({ types: ['epic'], limit: null })] });
    expect(meterText(w, [3, 0])).toBe('WIP 3 of 5 (1 on the way)');
  });

  it('puts what is on the way in before the over wording', () => {
    const w = wip({ slices: [wipSlice({ limit: 5, claimedInbound: 1 }), wipSlice({ types: ['epic'], limit: null })] });
    expect(meterText(w, [6, 0])).toBe('WIP 6 of 5 (1 on the way) · over');
  });

  it('reads both slices, the epic one prefixed with its types', () => {
    const w = wip({ slices: [wipSlice({ limit: 5, load: 3 }), wipSlice({ types: ['epic'], limit: 2, load: 1 })] });
    expect(meterText(w, [3, 1])).toBe('WIP 3 of 5 · epics 1 of 2');
  });

  it('reads the epic slice alone, still prefixed, when only it has a limit', () => {
    const w = wip({ slices: [wipSlice({ limit: null, load: 3 }), wipSlice({ types: ['epic'], limit: 2, load: 1 })] });
    expect(meterText(w, [3, 1])).toBe('WIP epics 1 of 2');
  });
});
