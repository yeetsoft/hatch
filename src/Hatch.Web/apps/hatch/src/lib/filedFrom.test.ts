import { describe, expect, it } from 'vitest';
import { filedFromCandidates } from './filedFrom';
import { ISSUE_TYPES } from '../types';
import type { IssueCard } from '../types';

/* Spread onto rather than written whole, so a new field on IssueCard is one
   edit here and not one per case - the same shape parents.test.ts uses. */
const card = (over: Partial<IssueCard> = {}): IssueCard => ({
  key: 'HATCH-1',
  projectKey: 'HATCH',
  type: 'epic',
  title: 'A card',
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
  ...over,
});

const keys = (cards: IssueCard[]): string[] => cards.map((c) => c.key);

const board = ISSUE_TYPES.map((type, i) => card({ key: `HATCH-${i + 1}`, type }));

describe('filedFromCandidates', () => {
  it('leaves out self', () => {
    expect(keys(filedFromCandidates(board, board[0].key))).not.toContain(board[0].key);
  });

  it('offers everything else, including another project', () => {
    const elsewhere = ISSUE_TYPES.map((type, i) => card({ key: `OTHER-${i + 1}`, projectKey: 'OTHER', type }));
    const all = [...elsewhere, ...board];
    expect(keys(filedFromCandidates(all, board[0].key))).toEqual(
      keys(all.filter((c) => c.key !== board[0].key)),
    );
  });

  it('keeps the order it was given', () => {
    const reversed = [...board].reverse();
    expect(keys(filedFromCandidates(reversed, board[0].key))).toEqual(
      keys(reversed.filter((c) => c.key !== board[0].key)),
    );
  });
});
