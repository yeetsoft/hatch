import { describe, expect, it } from 'vitest';
import { columnBlocked, moveBlocked } from './protectedColumns';
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
