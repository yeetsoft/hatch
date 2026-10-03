import { describe, expect, it } from 'vitest';
import { parentCandidates, parentEmptyMessage, parentHint } from './parents';
import { ISSUE_TYPES, LEGAL_PARENT_TYPES } from '../types';
import type { IssueCard, IssueType } from '../types';

/* Spread onto rather than written whole, so a new field on IssueCard is one
   edit here and not one per case - the same shape issuePicker.test.ts uses. */
const card = (over: Partial<IssueCard> = {}): IssueCard => ({
  key: 'HATCH-1',
  projectKey: 'HATCH',
  type: 'epic',
  title: 'A parent',
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
  priorityOwn: 'normal',
  priorityFrom: null,
  express: false,
  ...over,
});

/** One card of every type in HATCH, keyed by its position, so the cases below
    filter a board that has something of each. */
const board = ISSUE_TYPES.map((type, i) => card({ key: `HATCH-${i + 1}`, type }));

const keys = (cards: IssueCard[]): string[] => cards.map((c) => c.key);

/** The keys of the HATCH cards `type` may hang under, read off the table. */
const legalKeys = (type: IssueType): string[] =>
  keys(board.filter((c) => LEGAL_PARENT_TYPES[type].includes(c.type)));

describe('parentCandidates', () => {
  it('offers the same-project cards of a legal type', () => {
    for (const type of ISSUE_TYPES) {
      expect(keys(parentCandidates(board, 'HATCH', type))).toEqual(legalKeys(type));
    }
  });

  it('offers nothing from another project, whatever its type', () => {
    const elsewhere = ISSUE_TYPES.map((type, i) => card({ key: `OTHER-${i + 1}`, projectKey: 'OTHER', type }));
    for (const type of ISSUE_TYPES) {
      expect(keys(parentCandidates([...elsewhere, ...board], 'HATCH', type))).toEqual(legalKeys(type));
    }
  });

  it('offers only types the table allows', () => {
    const offered = parentCandidates(board, 'HATCH', 'bug');
    expect(offered.every((c) => LEGAL_PARENT_TYPES.bug.includes(c.type))).toBe(true);
  });

  it('leaves out self when one is named', () => {
    const [first] = legalKeys('epic');
    expect(keys(parentCandidates(board, 'HATCH', 'epic', first))).not.toContain(first);
  });

  it('offers every legal card when no self is named', () => {
    const [first] = legalKeys('epic');
    expect(keys(parentCandidates(board, 'HATCH', 'epic'))).toContain(first);
  });

  it('keeps the order it was given', () => {
    const reversed = [...board].reverse();
    expect(keys(parentCandidates(reversed, 'HATCH', 'bug'))).toEqual(
      keys(reversed.filter((c) => LEGAL_PARENT_TYPES.bug.includes(c.type))),
    );
  });
});

describe('parentHint', () => {
  it('puts an article on every type', () => {
    expect(parentHint('epic')).toBe('An epic hangs under an epic.');
    expect(parentHint('bug')).toBe('A bug hangs under an epic or a story.');
  });

  it('joins three with commas and a last "or"', () => {
    expect(parentHint('task')).toBe('A task hangs under a story, a bug or an epic.');
  });
});

describe('parentEmptyMessage', () => {
  it('names the project once one is chosen', () => {
    expect(parentEmptyMessage('HATCH', 'task')).toBe('Nothing in HATCH can be a parent of a task yet.');
  });

  it('names no project while none is chosen', () => {
    expect(parentEmptyMessage(undefined, 'task')).toBe('Nothing can be a parent until a project is chosen.');
  });
});
