/* Reading a playbook row: which move it speaks for. */

import type { Playbook } from '../types';

/**
 * A row whose two ends are the same column is the review playbook: what an
 * agent is told when a pull request in review has stopped merging cleanly or its
 * build has failed - one row answers both, and the runner brings the facts.
 *
 * The server only accepts that for the review column, and says which column may
 * when it is tried on any other - so nothing here checks which column it is,
 * and a row that reached the page with both ends equal is one the server let
 * through.
 */
export const isReviewPlaybook = (p: Pick<Playbook, 'fromStatusId' | 'toStatusId'>): boolean =>
  p.fromStatusId === p.toStatusId;

/**
 * The move a row is for, as a phrase: `todo to in progress`, or `review
 * review` for the review playbook. What a control's accessible name is
 * built from, so two rows on one page never share one.
 */
export function transitionLabel(p: Pick<Playbook, 'fromStatusId' | 'toStatusId' | 'fromStatusName' | 'toStatusName'>): string {
  return isReviewPlaybook(p) ? `${p.fromStatusName} review` : `${p.fromStatusName} to ${p.toStatusName}`;
}

/** What the budget field shows: blank for no cap, the number otherwise.
    Mirrors wipLimitDraft - see lib/wip.ts. */
export function budgetDraft(budget: number | null): string {
  return budget === null ? '' : String(budget);
}

/** The field, trimmed, as the wire's `budget` - blank clears it. */
export function budgetRequest(draft: string): string {
  return draft.trim();
}

/** What the brief limit field shows: blank for no cap, the number otherwise.
    Mirrors budgetDraft. */
export function briefLimitDraft(briefLimit: number | null): string {
  return briefLimit === null ? '' : String(briefLimit);
}

/** The field, trimmed, as the wire's `briefLimit` - blank clears it. */
export function briefLimitRequest(draft: string): string {
  return draft.trim();
}
