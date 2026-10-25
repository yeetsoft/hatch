import { describe, expect, it } from 'vitest';
import { carried, countWords, storyCount } from './storiesOnEpics';
import type { IssueCard, IssueType, Project, Status } from '../types';

const status = (id: number, sortOrder: number, opts: Partial<Status> = {}): Status => ({
  id,
  name: `status-${id}`,
  sortOrder,
  isTerminal: false,
  isDeferred: false,
  isWip: false,
  expressSkips: false,
  parentPulls: false,
  agentFiles: false,
  isImplementation: false,
  isProtected: false,
  mergesPullRequest: false,
  runnerTags: [],
  color: '#6b7280',
  ...opts,
});

const TODO = status(1, 0);
const MERGES = status(2, 1, { isProtected: true, mergesPullRequest: true });
const DONE = status(3, 2, { isTerminal: true });
const SHELVED = status(4, 3, { isDeferred: true });
const STATUSES = [TODO, MERGES, DONE, SHELVED];

const card = (
  key: string,
  type: IssueType,
  parentKey: string | null,
  opts: { statusId?: number; rank?: number; projectKey?: string } = {},
): IssueCard => ({
  key,
  projectKey: opts.projectKey ?? 'AER',
  type,
  title: `${key} title`,
  statusId: opts.statusId ?? TODO.id,
  rank: opts.rank ?? 1024,
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
  express: false,
  stalledAt: null,
  stalledWhy: null,
  held: false,
  pullRequestUrl: null,
  onePr: false,
  onePrOwn: null,
  onePrFrom: null,
});

const project = (storiesOnEpics: boolean) => ({ key: 'AER', storiesOnEpics }) as Project;
const ON = new Map([['AER', project(true)]]);

const keys = (cards: IssueCard[] | undefined) => (cards ?? []).map((c) => c.key);

describe('carried', () => {
  it('carries a story under an epic and lists it on the epic', () => {
    const cards = [card('E', 'epic', null), card('S', 'story', 'E')];

    const result = carried(cards, [], STATUSES, ON);

    expect([...result.carried]).toEqual(['S']);
    expect(keys(result.storiesByEpic.get('E'))).toEqual(['S']);
  });

  it('carries nothing when the project flag is off', () => {
    const cards = [card('E', 'epic', null), card('S', 'story', 'E')];

    const result = carried(cards, [], STATUSES, new Map([['AER', project(false)]]));

    expect(result.carried.size).toBe(0);
    expect(result.storiesByEpic.size).toBe(0);
  });

  it('carries nothing for a project missing from the map', () => {
    const cards = [card('E', 'epic', null), card('S', 'story', 'E')];

    const result = carried(cards, [], STATUSES, new Map());

    expect(result.carried.size).toBe(0);
    expect(result.storiesByEpic.size).toBe(0);
  });

  it.each(['bug', 'task', 'reflection'] as const)('does not carry a %s under an epic', (type) => {
    const cards = [card('E', 'epic', null), card('X', type, 'E')];

    const result = carried(cards, [], STATUSES, ON);

    expect(result.carried.size).toBe(0);
    expect(result.storiesByEpic.size).toBe(0);
  });

  it('does not carry a story under a reflection', () => {
    const cards = [card('R', 'reflection', null), card('S', 'story', 'R')];

    const result = carried(cards, [], STATUSES, ON);

    expect(result.carried.size).toBe(0);
    expect(result.storiesByEpic.size).toBe(0);
  });

  it('does not carry a sub-epic, but it carries its own stories', () => {
    const cards = [card('E', 'epic', null), card('SUB', 'epic', 'E'), card('S', 'story', 'SUB')];

    const result = carried(cards, [], STATUSES, ON);

    expect([...result.carried]).toEqual(['S']);
    expect(result.storiesByEpic.has('E')).toBe(false);
    expect(keys(result.storiesByEpic.get('SUB'))).toEqual(['S']);
  });

  it('does not carry a story whose epic stands in a deferred column', () => {
    const cards = [card('E', 'epic', null, { statusId: SHELVED.id }), card('S', 'story', 'E')];

    const result = carried(cards, [], STATUSES, ON);

    expect(result.carried.size).toBe(0);
    expect(result.storiesByEpic.size).toBe(0);
  });

  it('does not carry a story whose epic is not in the cards', () => {
    const result = carried([card('S', 'story', 'E')], [], STATUSES, ON);

    expect(result.carried.size).toBe(0);
    expect(result.storiesByEpic.size).toBe(0);
  });

  it('does not carry a story whose epic is only past the horizon', () => {
    const result = carried([card('S', 'story', 'E')], [card('E', 'epic', null)], STATUSES, ON);

    expect(result.carried.size).toBe(0);
    expect(result.storiesByEpic.size).toBe(0);
  });

  it('does not carry a story with no parent', () => {
    const result = carried([card('S', 'story', null)], [], STATUSES, ON);

    expect(result.carried.size).toBe(0);
  });

  it('orders an epic’s stories by rank, then key, whatever order they arrive in', () => {
    const cards = [
      card('E', 'epic', null),
      card('S-C', 'story', 'E', { rank: 2048 }),
      card('S-B', 'story', 'E', { rank: 1024 }),
      card('S-A', 'story', 'E', { rank: 1024 }),
      card('S-D', 'story', 'E', { rank: 10 }),
    ];

    const result = carried(cards, [], STATUSES, ON);

    expect(keys(result.storiesByEpic.get('E'))).toEqual(['S-D', 'S-A', 'S-B', 'S-C']);
  });

  it('lists a past-horizon story on its epic without carrying it, merged in order', () => {
    const cards = [card('E', 'epic', null), card('S-2', 'story', 'E', { rank: 2000 })];
    const past = [card('S-1', 'story', 'E', { rank: 1000 }), card('S-3', 'story', 'E', { rank: 3000 })];

    const result = carried(cards, past, STATUSES, ON);

    expect([...result.carried]).toEqual(['S-2']);
    expect(keys(result.storiesByEpic.get('E'))).toEqual(['S-1', 'S-2', 'S-3']);
  });

  it('applies the rule to a past-horizon card as it does to any other', () => {
    const cards = [card('E', 'epic', null)];
    const past = [card('T', 'task', 'E'), card('S', 'story', 'MISSING')];

    const result = carried(cards, past, STATUSES, ON);

    expect(result.storiesByEpic.size).toBe(0);
  });

  it('does not mutate its inputs', () => {
    const cards = [card('E', 'epic', null), card('S-B', 'story', 'E', { rank: 2 }), card('S-A', 'story', 'E', { rank: 1 })];
    const before = cards.map((c) => c.key);

    carried(cards, [], STATUSES, ON);

    expect(cards.map((c) => c.key)).toEqual(before);
  });
});

describe('storyCount', () => {
  it('counts only a story in a terminal column as done', () => {
    const stories = [
      card('A', 'story', 'E', { statusId: DONE.id }),
      card('B', 'story', 'E', { statusId: TODO.id }),
      card('C', 'story', 'E', { statusId: MERGES.id }),
    ];

    expect(storyCount(stories, STATUSES)).toEqual({ total: 3, done: 1 });
  });

  it('counts a story in a protected column as not done', () => {
    expect(storyCount([card('A', 'story', 'E', { statusId: MERGES.id })], STATUSES)).toEqual({ total: 1, done: 0 });
  });

  it('counts a story in an unknown column as not done', () => {
    expect(storyCount([card('A', 'story', 'E', { statusId: 99 })], STATUSES)).toEqual({ total: 1, done: 0 });
  });

  it('is zero and zero for no stories', () => {
    expect(storyCount([], STATUSES)).toEqual({ total: 0, done: 0 });
  });
});

describe('countWords', () => {
  it('says how many of the stories are done', () => {
    expect(countWords(3, 1)).toBe('1 of 3 done');
    expect(countWords(0, 0)).toBe('0 of 0 done');
    expect(countWords(2, 2)).toBe('2 of 2 done');
  });
});
