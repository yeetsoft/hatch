import { describe, expect, it } from 'vitest';
import { bulkBlocked, columnBlocked, moveBlocked } from './protectedColumns';
import type { IssueCard, Project, Status } from '../types';

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
  color: '#6b7280',
  ...opts,
});

const PROTECTED_MERGING = status(1, 0, { isProtected: true, mergesPullRequest: true });
const PROTECTED_ONLY = status(2, 1, { isProtected: true });
const PLAIN = status(3, 2);

describe('moveBlocked', () => {
  it('allows an approver with a pull request into a protected-and-merging column', () => {
    expect(moveBlocked(PROTECTED_MERGING, { canApprove: true }, { pullRequestUrl: 'https://example.com/pr/1' })).toBeNull();
  });

  it('blocks an approver with no pull request, naming the missing pull request', () => {
    expect(moveBlocked(PROTECTED_MERGING, { canApprove: true }, { pullRequestUrl: null })).toMatch(/pull request/i);
  });

  it('blocks a non-approver moving into a protected column, naming the approval requirement', () => {
    expect(moveBlocked(PROTECTED_ONLY, { canApprove: false }, { pullRequestUrl: null })).toMatch(/approver/i);
    expect(moveBlocked(PROTECTED_MERGING, { canApprove: false }, { pullRequestUrl: 'https://example.com/pr/1' })).toMatch(
      /approver/i,
    );
  });

  it('allows anyone into an unprotected, non-merging column', () => {
    expect(moveBlocked(PLAIN, { canApprove: false }, { pullRequestUrl: null })).toBeNull();
    expect(moveBlocked(PLAIN, { canApprove: true }, { pullRequestUrl: 'https://example.com/pr/1' })).toBeNull();
  });

  it('blocks against a protected column the same way a non-approver is blocked, when project is missing', () => {
    expect(moveBlocked(PROTECTED_ONLY, null, { pullRequestUrl: null })).toMatch(/approver/i);
    expect(moveBlocked(PROTECTED_ONLY, undefined, { pullRequestUrl: null })).toMatch(/approver/i);
  });
});

const project = (over: Partial<Project> = {}): Project => ({
  id: 1,
  key: 'AER',
  name: 'Aerial',
  issueCount: 0,
  createdAt: '2026-01-01T00:00:00Z',
  color: null,
  icon: null,
  logoUpdatedAt: null,
  repositories: [],
  members: [],
  canApprove: false,
  canAdminister: false,
  ...over,
});

const card = (over: Partial<IssueCard> = {}): IssueCard => ({
  key: 'AER-1',
  projectKey: 'AER',
  type: 'task',
  title: 'AER-1',
  statusId: 1,
  rank: 0,
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
  stalledAt: null,
  stalledWhy: null,
  held: false,
  pullRequestUrl: null,
  ...over,
});

describe('columnBlocked', () => {
  it('is null for every status when nothing is being dragged', () => {
    const projectsByKey = new Map([['AER', project({ canApprove: false })]]);
    expect(columnBlocked(PROTECTED_ONLY, null, projectsByKey)).toBeNull();
    expect(columnBlocked(PROTECTED_MERGING, null, projectsByKey)).toBeNull();
    expect(columnBlocked(PLAIN, null, projectsByKey)).toBeNull();
  });

  it('allows an approver with a pull request onto a protected-and-merging column', () => {
    const projectsByKey = new Map([['AER', project({ canApprove: true })]]);
    const dragging = card({ pullRequestUrl: 'https://example.com/pr/1' });
    expect(columnBlocked(PROTECTED_MERGING, dragging, projectsByKey)).toBeNull();
  });

  it('blocks a non-approver with the approval reason', () => {
    const projectsByKey = new Map([['AER', project({ canApprove: false })]]);
    expect(columnBlocked(PROTECTED_ONLY, card(), projectsByKey)).toMatch(/approver/i);
  });

  it('blocks a card with no pull request onto a merge-only column with the merge reason', () => {
    const mergeOnly = status(4, 3, { mergesPullRequest: true });
    const projectsByKey = new Map([['AER', project({ canApprove: true })]]);
    expect(columnBlocked(mergeOnly, card({ pullRequestUrl: null }), projectsByKey)).toMatch(/pull request/i);
  });

  it('is unblocked onto a plain column regardless of approval', () => {
    const projectsByKey = new Map([['AER', project({ canApprove: false })]]);
    expect(columnBlocked(PLAIN, card(), projectsByKey)).toBeNull();
  });

  it('blocks a dragging card whose project is absent from the map, the same as a non-approver', () => {
    const projectsByKey = new Map<string, Project>();
    expect(columnBlocked(PROTECTED_ONLY, card(), projectsByKey)).toMatch(/approver/i);
  });
});

describe('bulkBlocked', () => {
  it('is null for every status when there is nothing to check', () => {
    const projectsByKey = new Map([['AER', project({ canApprove: false })]]);
    expect(bulkBlocked(PROTECTED_ONLY, [], projectsByKey)).toBeNull();
    expect(bulkBlocked(PROTECTED_MERGING, [], projectsByKey)).toBeNull();
    expect(bulkBlocked(PLAIN, [], projectsByKey)).toBeNull();
  });

  it('blocks on a protected column when one result among others cannot approve, naming that result first', () => {
    const projectsByKey = new Map([
      ['AER', project({ key: 'AER', canApprove: true })],
      ['HA', project({ key: 'HA', canApprove: false })],
    ]);
    const issues = [
      card({ key: 'AER-1', projectKey: 'AER', statusId: 99, pullRequestUrl: 'https://example.com/pr/1' }),
      card({ key: 'HA-1', projectKey: 'HA', statusId: 99, pullRequestUrl: 'https://example.com/pr/2' }),
    ];
    expect(bulkBlocked(PROTECTED_ONLY, issues, projectsByKey)).toMatch(/^HA-1:.*approver/i);
    expect(bulkBlocked(PROTECTED_MERGING, issues, projectsByKey)).toMatch(/^HA-1:.*approver/i);
  });

  it('blocks on a merge-only column when one result among others has no pull request', () => {
    const projectsByKey = new Map([['AER', project({ canApprove: true })]]);
    const mergeOnly = status(4, 3, { mergesPullRequest: true });
    const issues = [
      card({ key: 'AER-1', statusId: 99, pullRequestUrl: 'https://example.com/pr/1' }),
      card({ key: 'AER-2', statusId: 99, pullRequestUrl: null }),
    ];
    expect(bulkBlocked(mergeOnly, issues, projectsByKey)).toMatch(/^AER-2:.*pull request/i);
  });

  it('stays unblocked when every result passes both gates', () => {
    const projectsByKey = new Map([['AER', project({ canApprove: true })]]);
    const issues = [
      card({ key: 'AER-1', statusId: 99, pullRequestUrl: 'https://example.com/pr/1' }),
      card({ key: 'AER-2', statusId: 99, pullRequestUrl: 'https://example.com/pr/2' }),
    ];
    expect(bulkBlocked(PLAIN, issues, projectsByKey)).toBeNull();
    expect(bulkBlocked(PROTECTED_MERGING, issues, projectsByKey)).toBeNull();
  });

  it('does not count a result already standing in the target column against that column, only against others', () => {
    const projectsByKey = new Map([['AER', project({ canApprove: false })]]);
    const already = card({ key: 'AER-1', statusId: PROTECTED_ONLY.id, pullRequestUrl: null });
    expect(bulkBlocked(PROTECTED_ONLY, [already], projectsByKey)).toBeNull();
    expect(bulkBlocked(PROTECTED_MERGING, [already], projectsByKey)).toMatch(/approver/i);
  });

  it('blocks on a protected column when a result project is absent from the map, the same as a non-approver', () => {
    const projectsByKey = new Map<string, Project>();
    expect(bulkBlocked(PROTECTED_ONLY, [card({ statusId: 99 })], projectsByKey)).toMatch(/approver/i);
  });
});
