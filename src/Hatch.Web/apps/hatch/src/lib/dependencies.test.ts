import { describe, expect, it } from 'vitest';
import { dependencyCandidates, dependencyLookupFilter } from './dependencies';
import type { IssueCard } from '../types';

const TODO = 1;

const card = (key: string, parentKey: string | null, projectKey = 'AER'): IssueCard => ({
  key,
  projectKey,
  type: 'task',
  title: `${key} title`,
  statusId: TODO,
  rank: 1024,
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
});

const keys = (cards: IssueCard[]) => cards.map((c) => c.key);

/* An epic (AER-1) with two stories under it (AER-2, AER-5) and a task under
   each (AER-3, AER-6), plus a loose issue and one in another project. */
const cards: IssueCard[] = [
  card('AER-1', null),
  card('AER-2', 'AER-1'),
  card('AER-3', 'AER-2'),
  card('AER-5', 'AER-1'),
  card('AER-6', 'AER-5'),
  card('AER-9', null),
  card('OPS-4', null, 'OPS'),
];

describe('dependencyCandidates', () => {
  it('never offers the issue itself', () => {
    expect(keys(dependencyCandidates(cards, 'AER-9', []))).not.toContain('AER-9');
  });

  it('leaves out ancestors at any depth', () => {
    // An edge upwards is a deadlock with a nicer name: a parent is not done
    // until its work is.
    const offered = keys(dependencyCandidates(cards, 'AER-3', []));

    expect(offered).not.toContain('AER-2');
    expect(offered).not.toContain('AER-1');
  });

  it('leaves out descendants at any depth', () => {
    const offered = keys(dependencyCandidates(cards, 'AER-1', []));

    expect(offered).toEqual(['AER-9', 'OPS-4']);
  });

  it('still offers a sibling and its line', () => {
    // The whole point of the feature: two stories under one epic that must
    // land in order say so with an edge.
    expect(keys(dependencyCandidates(cards, 'AER-2', []))).toEqual(['AER-5', 'AER-6', 'AER-9', 'OPS-4']);
  });

  it('leaves out what it already waits on', () => {
    expect(keys(dependencyCandidates(cards, 'AER-9', ['OPS-4']))).toEqual([
      'AER-1',
      'AER-2',
      'AER-3',
      'AER-5',
      'AER-6',
    ]);
  });

  it('offers a card in another project', () => {
    // An edge may cross a project where a parent may not - containment and
    // ordering are different claims.
    expect(keys(dependencyCandidates(cards, 'AER-9', []))).toContain('OPS-4');
  });

  it('terminates on a parent cycle rather than spinning', () => {
    // Parenting refuses to close a loop, but one that got in some other way
    // must produce a wrong answer rather than hang the browser.
    const looped = [card('AER-1', 'AER-2'), card('AER-2', 'AER-1'), card('AER-9', null)];

    expect(keys(dependencyCandidates(looped, 'AER-1', []))).toEqual(['AER-9']);
  });

  it('offers everything on a board where nothing is related', () => {
    const loose = [card('AER-7', null), card('AER-8', null)];

    expect(keys(dependencyCandidates(loose, 'AER-7', []))).toEqual(['AER-8']);
  });
});

/* The same refusal, run over a row a lookup found rather than a card on the
   board - so the only thing worth re-checking is that it is the same refusal,
   not that the walk works, which `dependencyCandidates`'s own tests already
   cover. */
const row = (key: string) => ({ key, title: `${key} title`, type: 'task' });

describe('dependencyLookupFilter', () => {
  it('refuses the issue itself', () => {
    expect(dependencyLookupFilter(cards, 'AER-9', [])(row('AER-9'))).toBe(false);
  });

  it('refuses what it already waits on', () => {
    expect(dependencyLookupFilter(cards, 'AER-9', ['OPS-4'])(row('OPS-4'))).toBe(false);
  });

  it('refuses an ancestor among the cards', () => {
    expect(dependencyLookupFilter(cards, 'AER-3', [])(row('AER-1'))).toBe(false);
  });

  it('refuses a descendant among the cards', () => {
    expect(dependencyLookupFilter(cards, 'AER-1', [])(row('AER-3'))).toBe(false);
  });

  it('passes a key no card holds', () => {
    expect(dependencyLookupFilter(cards, 'AER-9', [])(row('AER-404'))).toBe(true);
  });

  it('passes an unrelated card, closed or not', () => {
    expect(dependencyLookupFilter(cards, 'AER-9', [])(row('OPS-4'))).toBe(true);
  });
});
