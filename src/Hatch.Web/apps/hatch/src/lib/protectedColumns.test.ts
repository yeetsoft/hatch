import { describe, expect, it } from 'vitest';
import { moveBlocked } from './protectedColumns';
import type { Status } from '../types';

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
