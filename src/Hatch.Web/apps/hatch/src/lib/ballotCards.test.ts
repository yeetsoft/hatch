import { describe, expect, it } from 'vitest';
import { cardQueue, holdsWords, optionsRecommendedFirst, rootOf, thenWords } from './ballotCards';
import type { BallotNode, BallotQuestion } from '../types';

const ask = (id: number): BallotQuestion => ({
  question: {
    id,
    issueKey: 'PRJ-1',
    issueTitle: 'A task',
    body: 'Which?',
    askedBy: 'a-runner',
    askedAt: '2026-01-10T09:00:00Z',
    options: null,
    answers: [],
    level: 'hold',
    lookBack: false,
  },
  holds: 1,
  wentAheadOn: null,
  issueStatus: null,
  closedAt: null,
  merged: null,
  landings: null,
});

const ids = (qs: BallotQuestion[]) => qs.map((q) => q.question.id);

const node = (key: string, children: BallotNode[] = []): BallotNode => ({
  key,
  projectKey: 'PRJ',
  type: 'epic',
  title: key,
  excerpt: '',
  isTerminal: false,
  children,
  questions: [],
});

describe('cardQueue', () => {
  const all = [ask(1), ask(2), ask(3), ask(4)];

  it('is the server order when nothing is skipped', () => {
    expect(ids(cardQueue(all, []))).toEqual([1, 2, 3, 4]);
  });

  it('puts one skipped question after the last', () => {
    expect(ids(cardQueue(all, [1]))).toEqual([2, 3, 4, 1]);
  });

  it('puts two skipped questions last, in the order they were skipped', () => {
    expect(ids(cardQueue(all, [3, 1]))).toEqual([2, 4, 3, 1]);
  });

  it('drops a skipped id the server no longer lists', () => {
    expect(ids(cardQueue(all, [9, 2]))).toEqual([1, 3, 4, 2]);
  });
});

describe('rootOf', () => {
  const roots = [node('PRJ-1', [node('PRJ-2', [node('PRJ-3')])]), node('PRJ-9')];

  it('finds the root by its own key', () => {
    expect(rootOf(roots, 'PRJ-9')?.key).toBe('PRJ-9');
  });

  it('finds the root holding a deep child', () => {
    expect(rootOf(roots, 'PRJ-3')?.key).toBe('PRJ-1');
  });

  it('is null where no root holds the key', () => {
    expect(rootOf(roots, 'PRJ-404')).toBeNull();
  });
});

describe('optionsRecommendedFirst', () => {
  it('moves the recommended option up and keeps the rest in order', () => {
    const options = [
      { label: 'a', detail: null, recommended: false },
      { label: 'b', detail: null, recommended: true },
      { label: 'c', detail: null, recommended: false },
    ];
    expect(optionsRecommendedFirst(options).map((o) => o.label)).toEqual(['b', 'a', 'c']);
  });
});

describe('holdsWords', () => {
  it('says issue once and issues otherwise', () => {
    expect(holdsWords(3)).toBe('Holds 3 issues');
    expect(holdsWords(1)).toBe('Holds 1 issue');
    expect(holdsWords(0)).toBe('Holds nothing');
  });
});

describe('thenWords', () => {
  it('names both clauses', () => {
    expect(thenWords(2, 1)).toBe('Then 2 more to answer, and 1 decided without you to look over');
  });

  it('leaves out a clause at zero', () => {
    expect(thenWords(2, 0)).toBe('Then 2 more to answer');
    expect(thenWords(0, 1)).toBe('Then 1 decided without you to look over');
  });

  it('says nothing else is waiting when both are zero', () => {
    expect(thenWords(0, 0)).toBe('Nothing else is waiting');
  });
});
