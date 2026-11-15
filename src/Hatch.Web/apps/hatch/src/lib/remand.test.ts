import { describe, expect, it } from 'vitest';
import { dreamHref, remandColumn, remandOffer, remandRequest, remandTitle, remandedSentence, scopedDreamHref, showCloud } from './remand';
import type { Board, DreamRemandResult, IssueCard, Status } from '../types';

const TODO = 1;
const DOING = 2;
const DONE = 3;
const SHELVED = 5;
const PARKED = 6;

const status = (id: number, name: string, over: Partial<Status> = {}): Status => ({
  id, name, sortOrder: id, isTerminal: false, isDeferred: false, isWip: false, expressSkips: false, parentPulls: false, agentFiles: false, isImplementation: false, isProtected: false, mergesPullRequest: false, runnerTags: [], color: '#888888', ...over,
});

const plain = [status(TODO, 'To Do'), status(DOING, 'In Progress'), status(DONE, 'Done', { isTerminal: true })];
const one = [...plain, status(SHELVED, 'Deferred', { isDeferred: true })];
const two = [...one, status(PARKED, 'Parked', { isDeferred: true })];

const card = (key: string, statusId: number, parentKey: string | null): IssueCard =>
  ({
    key, projectKey: 'AER', type: 'task', title: `${key} title`, statusId, rank: 1024, parentKey, readyAt: null, dueAt: null, openQuestions: 0, assignee: null, claim: null, expedited: false, priority: 'normal', priorityOwn: 'normal', priorityFrom: null, preApproval: 'none', preApprovalOwn: 'none', preApprovalFrom: null, runnerTags: [], runnerTagsOwn: [],
  }) as unknown as IssueCard;

const boardOf = (statuses: Status[], issues: IssueCard[]) => ({ statuses, issues }) as unknown as Board;

const tree = [
  card('AER-1', DOING, null),
  card('AER-2', TODO, 'AER-1'),
  card('AER-3', DONE, 'AER-1'),
  card('AER-4', SHELVED, 'AER-1'),
  card('AER-5', DOING, 'AER-2'),
];

describe('showCloud', () => {
  it('is not drawn when no column is deferred', () => {
    expect(showCloud(plain)).toBe(false);
  });

  it('is drawn when one is', () => {
    expect(showCloud(one)).toBe(true);
  });
});

describe('remandOffer', () => {
  it('is null with no deferred column', () => {
    expect(remandOffer(boardOf(plain, tree), 'AER-1')).toBeNull();
  });

  it('is null for a card that is not on the board', () => {
    expect(remandOffer(boardOf(one, tree), 'AER-99')).toBeNull();
  });

  it('has no chooser with one deferred column', () => {
    const offer = remandOffer(boardOf(one, tree), 'AER-1')!;
    expect(offer.columns).toHaveLength(1);
    expect(offer.statusId).toBe(SHELVED);
    expect(offer.title).toBe('AER-1 title');
  });

  it('defaults to the first deferred column and honours a chosen one', () => {
    const board = boardOf(two, tree);
    expect(remandOffer(board, 'AER-1')!.statusId).toBe(SHELVED);
    expect(remandOffer(board, 'AER-1', PARKED)!.statusId).toBe(PARKED);
    expect(remandOffer(board, 'AER-1', TODO)!.statusId).toBe(SHELVED);
  });

  it('lists the open descendants in board order, leaving out the stopped ones', () => {
    const offer = remandOffer(boardOf(one, tree), 'AER-1')!;
    expect(offer.cards.map((c) => c.key)).toEqual(['AER-2', 'AER-5']);
  });

  it('lists none when every descendant has stopped, and still offers', () => {
    const board = boardOf(one, [card('AER-1', DOING, null), card('AER-2', DONE, 'AER-1'), card('AER-3', SHELVED, 'AER-1')]);
    const offer = remandOffer(board, 'AER-1')!;
    expect(offer).not.toBeNull();
    expect(offer.cards).toEqual([]);
  });

  it('is offered for a card that is already stopped', () => {
    expect(remandOffer(boardOf(one, tree), 'AER-3')).not.toBeNull();
  });
});

describe('remandRequest', () => {
  it('names the column and exactly the listed keys', () => {
    const offer = remandOffer(boardOf(two, tree), 'AER-1', PARKED)!;
    expect(remandRequest(offer)).toEqual({ statusId: PARKED, keys: ['AER-2', 'AER-5'] });
  });

  it('sends an empty list when nothing is under it', () => {
    const offer = remandOffer(boardOf(one, [card('AER-1', DOING, null)]), 'AER-1')!;
    expect(remandRequest(offer)).toEqual({ statusId: SHELVED, keys: [] });
  });
});

describe('words', () => {
  const offer = remandOffer(boardOf(two, tree), 'AER-1', PARKED)!;
  const result = (changed: string[]): DreamRemandResult => ({ dream: {} as DreamRemandResult['dream'], changed, unchanged: [] });

  it('titles the dialog with the key', () => {
    expect(remandTitle(offer)).toBe('Remand AER-1 to a dream?');
  });

  it('names the chosen column', () => {
    expect(remandColumn(offer).name).toBe('Parked');
  });

  it('counts the issues under the root, one, several and none', () => {
    expect(remandedSentence('AER-1', result(['AER-1', 'AER-2']))).toBe('AER-1 remanded to a dream, with 1 issue under it.');
    expect(remandedSentence('AER-1', result(['AER-1', 'AER-2', 'AER-5']))).toBe('AER-1 remanded to a dream, with 2 issues under it.');
    expect(remandedSentence('AER-1', result(['AER-1']))).toBe('AER-1 remanded to a dream.');
    expect(remandedSentence('AER-1', result([]))).toBe('AER-1 remanded to a dream.');
  });

  it('links to the dreams remanded from the root', () => {
    expect(dreamHref('AER-1')).toBe('/dreams?remandedFrom=AER-1');
  });

  it('keeps the scope on the link, joined to the query it already has', () => {
    expect(scopedDreamHref('/dreams?remandedFrom=AER-1', [])).toBe('/dreams?remandedFrom=AER-1');
    const scoped = scopedDreamHref('/dreams?remandedFrom=AER-1', ['AER']);
    expect(scoped.startsWith('/dreams?')).toBe(true);
    expect(scoped).toContain('remandedFrom=AER-1');
    expect(scoped).toContain('scope=AER');
    expect(scoped.match(/\?/g)).toHaveLength(1);
  });
});
