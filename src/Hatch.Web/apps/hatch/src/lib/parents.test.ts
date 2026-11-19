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
  preApproval: 'none',
  preApprovalOwn: 'none',
  preApprovalFrom: null,
  localModel: 'none',
  localModelOwn: 'none',
  localModelFrom: null,
  humanWork: 'agent',
  humanWorkOwn: 'inherit',
  humanWorkFrom: null,
  runnerTags: [],
  runnerTagsOwn: [],
  runnerTagsFrom: [],
  dangerousOwn: null,
  dangerous: null,
  dangerousFrom: null,
  omittedRunnersOwn: [],
  omittedRunners: [],
  express: false,
  stalledAt: null,
  stalledWhy: null,
  held: false,
  pullRequestUrl: null,
  onePr: false,
  onePrOwn: null,
  onePrFrom: null,
  createdBy: 'Britta Perry',
  projectPriority: 'normal',
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
  it('offers the cards of a legal type', () => {
    for (const type of ISSUE_TYPES) {
      expect(keys(parentCandidates(board, 'HATCH', type))).toEqual(legalKeys(type));
    }
  });

  it('offers an epic the ambitions, and an ambition nothing', () => {
    expect(keys(parentCandidates(board, 'HATCH', 'epic'))).toContain('HATCH-1');
    expect(board[0].type).toBe('ambition');
    expect(parentCandidates(board, 'HATCH', 'ambition')).toEqual([]);
  });

  it('offers cards from every project, whatever the project', () => {
    const elsewhere = ISSUE_TYPES.map((type, i) => card({ key: `OTHER-${i + 1}`, projectKey: 'OTHER', type }));
    for (const type of ISSUE_TYPES) {
      const offered = parentCandidates([...elsewhere, ...board], 'HATCH', type);
      expect(new Set(offered.map((c) => c.projectKey))).toEqual(
        new Set(LEGAL_PARENT_TYPES[type].length > 0 ? ['HATCH', 'OTHER'] : []),
      );
      expect(offered.length).toBe(legalKeys(type).length * 2);
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

  it('lists the own project first, the others A to Z, each by key number', () => {
    const mixed = [
      card({ key: 'ZED-1', projectKey: 'ZED' }),
      card({ key: 'HATCH-10', projectKey: 'HATCH' }),
      card({ key: 'AER-2', projectKey: 'AER' }),
      card({ key: 'MID-5', projectKey: 'MID' }),
      card({ key: 'HATCH-9', projectKey: 'HATCH' }),
      card({ key: 'AER-10', projectKey: 'AER' }),
    ];
    expect(keys(parentCandidates(mixed, 'MID', 'epic'))).toEqual([
      'MID-5',
      'AER-2',
      'AER-10',
      'HATCH-9',
      'HATCH-10',
      'ZED-1',
    ]);
  });

  it('does not depend on the order it was given', () => {
    const reversed = [...board].reverse();
    expect(keys(parentCandidates(reversed, 'HATCH', 'bug'))).toEqual(legalKeys('bug'));
  });
});

describe('parentHint', () => {
  it('puts an article on every type', () => {
    expect(parentHint('epic')).toBe('An epic hangs under an epic, a reflection or an ambition, in any project.');
    expect(parentHint('bug')).toBe('A bug hangs under an epic, a story or a reflection, in any project.');
    expect(parentHint('reflection')).toBe('A reflection hangs under an epic, in any project.');
  });

  it('says an ambition takes no parent, and offers an epic its ambitions', () => {
    expect(parentHint('ambition')).toBe('An ambition takes no parent.');
    expect(parentHint('epic')).toBe('An epic hangs under an epic, a reflection or an ambition, in any project.');
  });

  it('joins three with commas and a last "or"', () => {
    expect(parentHint('task')).toBe('A task hangs under a story, a bug, an epic or a reflection, in any project.');
  });
});

describe('parentEmptyMessage', () => {
  it('names the project once one is chosen', () => {
    expect(parentEmptyMessage('HATCH', 'task')).toBe('Nothing can be a parent of a task yet.');
  });

  it('names no project while none is chosen', () => {
    expect(parentEmptyMessage(undefined, 'task')).toBe('Nothing can be a parent until a project is chosen.');
  });
});
