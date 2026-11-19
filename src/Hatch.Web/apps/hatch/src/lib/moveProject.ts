/* What a move is about to carry along, and what it would take along uninvited.

   The dialog that moves an issue between projects offers to carry its
   descendants with it, or to leave them where they are, still under the moved
   issue; this module is the whole of deciding what that choice means - which
   issues are actually moving, and which of those are worth stopping to look
   at: an open pull request that would point at the wrong project, or a live
   claim that Confirm should refuse to disturb. A move never changes the
   issue's own parent, so the summary only names the one it keeps.

   The descendant list arrives already walked - the server's own
   Rollup.DescendantIdsAsync, fetched through `GET /issues?ancestorKey=` - so
   there is no tree to climb here, only arithmetic over a list it is handed. */

import type { IssueClaim } from '../types';

export interface MoveCandidate {
  key: string;
  title: string;
  claim: IssueClaim | null;
  pullRequestUrl: string | null;
}

export interface MoveSummary {
  /** The key the issue sits under and keeps, or null when it has no parent -
      a parent may stand in another project, so a move leaves it alone. */
  keptParentKey: string | null;
  /** null when the issue has no descendants at all - the dialog draws no
      radio choice in that case. */
  descendantChoice: { count: number } | null;
  /** Every issue in the *effective* moving set (see movingSet) that carries
      an open pull request. */
  pullRequests: MoveCandidate[];
  /** Every issue in the *effective* moving set under a live claim - Confirm
      is disabled whenever this is non-empty. */
  liveClaims: MoveCandidate[];
}

/**
 * Which issues the current choice actually carries along: just the one being
 * moved, or it and every one of its descendants.
 *
 * The one place that decides this, so `moveSummary` can derive both the
 * pull-request list and the claim list from the same array and never
 * disagree with itself about what is moving.
 */
export function movingSet(
  issue: MoveCandidate,
  descendants: MoveCandidate[],
  moveDescendants: boolean,
): MoveCandidate[] {
  return moveDescendants ? [issue, ...descendants] : [issue];
}

/**
 * What the dialog has to say about a move before it is confirmed: the parent it
 * keeps, whether there is a descendant choice to draw at all, and
 * which issues in the moving set are worth stopping for.
 */
export function moveSummary(
  issue: MoveCandidate & { parentKey: string | null },
  descendants: MoveCandidate[],
  moveDescendants: boolean,
): MoveSummary {
  const set = movingSet(issue, descendants, moveDescendants);
  return {
    keptParentKey: issue.parentKey,
    descendantChoice: descendants.length > 0 ? { count: descendants.length } : null,
    pullRequests: set.filter((c) => c.pullRequestUrl !== null),
    liveClaims: set.filter((c) => c.claim !== null),
  };
}
