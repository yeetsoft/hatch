import { describe, expect, it } from 'vitest';
import { lineage } from './lineage';
import type { IssueCard } from '../types';

const TODO = 1;
const DOING = 2;

// Verbatim from closeSubtree.test.ts, same defaults.
const card = (key: string, statusId: number, parentKey: string | null, rank = 1024): IssueCard => ({
  key,
  projectKey: 'AER',
  type: 'task',
  title: `${key} title`,
  statusId,
  rank,
  parentKey,
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
  express: false,
  stalledAt: null,
  stalledWhy: null,
  held: false,
  pullRequestUrl: null,
  onePr: false,
  onePrOwn: null,
  onePrFrom: null,
});

const keys = (cards: IssueCard[]) => cards.map((c) => c.key);
const depths = (cards: ReturnType<typeof lineage>) => cards.map((c) => c.depth);

describe('lineage', () => {
  it('leaves a flat column unchanged, every card at depth 0', () => {
    const cards = [card('A', TODO, null), card('B', TODO, null), card('C', TODO, null)];

    expect(keys(lineage(cards, cards))).toEqual(['A', 'B', 'C']);
    expect(depths(lineage(cards, cards))).toEqual([0, 0, 0]);
  });

  it('pulls a parent and its children adjacent, leaving strangers in place', () => {
    const p = card('P', TODO, null);
    const x = card('X', TODO, null);
    const c1 = card('C1', TODO, 'P');
    const y = card('Y', TODO, null);
    const c2 = card('C2', TODO, 'P');
    const column = [p, x, c1, y, c2];

    const result = lineage(column, column);

    expect(keys(result)).toEqual(['P', 'C1', 'C2', 'X', 'Y']);
    expect(depths(result)).toEqual([0, 1, 1, 0, 0]);
  });

  it('groups children whose parent sits in another column, with no row for the parent', () => {
    const s = card('S', TODO, null);
    const t1 = card('T1', DOING, 'S');
    const t2 = card('T2', DOING, 'S');
    const all = [s, t1, t2];
    const column = [t1, t2];

    const result = lineage(all, column);

    expect(keys(result)).toEqual(['T1', 'T2']);
    expect(depths(result)).toEqual([0, 0]);
  });

  it('groups children whose parent was filtered out of the same column - the filtered-out twin of the other-column case', () => {
    const p = card('P', DOING, null);
    const t1 = card('T1', DOING, 'P');
    const t2 = card('T2', DOING, 'P');
    const all = [p, t1, t2];
    const column = [t1, t2];

    const result = lineage(all, column);

    expect(keys(result)).toEqual(['T1', 'T2']);
    expect(depths(result)).toEqual([0, 0]);
  });

  it('keeps a card whose parent is absent from the whole array - folded off the board by the server - at depth 0', () => {
    const orphan = card('C', TODO, 'GONE');

    const result = lineage([orphan], [orphan]);

    expect(keys(result)).toEqual(['C']);
    expect(depths(result)).toEqual([0]);
  });

  it('skips a missing generation rather than counting it', () => {
    const e = card('E', TODO, null);
    const s = card('S', DOING, 'E');
    const t = card('T', TODO, 'S');
    const all = [e, s, t];
    const column = [e, t];

    const result = lineage(all, column);

    expect(keys(result)).toEqual(['E', 'T']);
    expect(depths(result)).toEqual([0, 1]);
  });

  it('keeps a priority-floated card at its ancestry depth even when served ahead of its parent', () => {
    const c1 = card('C1', TODO, 'P');
    const x = card('X', TODO, null);
    const p = card('P', TODO, null);
    const column = [c1, x, p];

    const result = lineage(column, column);

    expect(keys(result)).toEqual(['C1', 'P', 'X']);
    expect(depths(result)).toEqual([1, 0, 0]);
  });

  it('terminates on a parentKey cycle, and returns each card once', () => {
    const a = card('A', TODO, 'B');
    const b = card('B', TODO, 'A');
    const cards = [a, b];

    const result = lineage(cards, cards);

    expect(keys(result).sort()).toEqual(['A', 'B']);
  });
});
