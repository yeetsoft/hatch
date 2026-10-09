import { describe, expect, it } from 'vitest';
import { movingSet, moveSummary } from './moveProject';
import type { MoveCandidate } from './moveProject';
import type { IssueClaim } from '../types';

const claim: IssueClaim = {
  claimedBy: 'Ada',
  runner: 'Ada',
  claimedAt: '2026-10-08T00:00:00Z',
  heartbeatAt: '2026-10-08T00:00:00Z',
  chatter: null,
  chatterAt: null,
  ttlSeconds: 600,
};

const candidate = (key: string, overrides: Partial<MoveCandidate> = {}): MoveCandidate => ({
  key,
  title: `${key} title`,
  claim: null,
  pullRequestUrl: null,
  ...overrides,
});

/* The issue being moved, with a parent and two descendants - one carrying a
   pull request, one carrying a live claim. */
const issue = { ...candidate('AER-1'), parentKey: 'AER-0' };
const withPr = candidate('AER-2', { pullRequestUrl: 'https://example.invalid/pr/1' });
const withClaim = candidate('AER-3', { claim });
const descendants = [withPr, withClaim];

describe('movingSet', () => {
  it('returns just the issue when descendants are not carried along', () => {
    expect(movingSet(issue, descendants, false)).toEqual([issue]);
  });

  it('returns the issue followed by its descendants, in order, when they are carried along', () => {
    expect(movingSet(issue, descendants, true)).toEqual([issue, withPr, withClaim]);
  });
});

describe('moveSummary', () => {
  it('counts descendants the same way regardless of whether they are carried along', () => {
    expect(moveSummary(issue, descendants, true).descendantChoice).toEqual({ count: 2 });
    expect(moveSummary(issue, descendants, false).descendantChoice).toEqual({ count: 2 });
  });

  it('draws no descendant choice when the issue has no descendants', () => {
    expect(moveSummary(issue, [], true).descendantChoice).toBeNull();
    expect(moveSummary(issue, [], false).descendantChoice).toBeNull();
  });

  it('names the parent it detaches from', () => {
    expect(moveSummary(issue, descendants, true).detachedFromParentKey).toBe('AER-0');
  });

  it('is null when the issue has no parent', () => {
    const orphan = { ...issue, parentKey: null };

    expect(moveSummary(orphan, descendants, true).detachedFromParentKey).toBeNull();
  });

  it('names the root and the PR-carrying descendant when descendants are carried along', () => {
    const rootWithPr = { ...issue, pullRequestUrl: 'https://example.invalid/pr/0' };

    expect(moveSummary(rootWithPr, descendants, true).pullRequests).toEqual([rootWithPr, withPr]);
  });

  it('drops the descendant from pullRequests but keeps the root when descendants are not carried along', () => {
    const rootWithPr = { ...issue, pullRequestUrl: 'https://example.invalid/pr/0' };

    expect(moveSummary(rootWithPr, descendants, false).pullRequests).toEqual([rootWithPr]);
  });

  it('has no live claims when nothing in the moving set is claimed', () => {
    expect(moveSummary(issue, [withPr], true).liveClaims).toEqual([]);
  });

  it('names the claimed descendant when descendants are carried along, and stops naming it when they are not', () => {
    expect(moveSummary(issue, descendants, true).liveClaims).toEqual([withClaim]);
    expect(moveSummary(issue, descendants, false).liveClaims).toEqual([]);
  });

  it('names the root itself when it carries the claim, regardless of the choice', () => {
    const rootWithClaim = { ...issue, claim };

    expect(moveSummary(rootWithClaim, descendants, true).liveClaims).toEqual([rootWithClaim, withClaim]);
    expect(moveSummary(rootWithClaim, descendants, false).liveClaims).toEqual([rootWithClaim]);
  });
});
