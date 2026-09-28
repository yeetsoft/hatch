/* Reading a playbook row: which move it speaks for. */

import type { Playbook } from '../types';

/**
 * A row whose two ends are the same column is the conflict playbook: what an
 * agent is told when a pull request in review has stopped merging cleanly.
 *
 * The server only accepts that for the review column, and says which column may
 * when it is tried on any other - so nothing here checks which column it is,
 * and a row that reached the page with both ends equal is one the server let
 * through.
 */
export const isConflictPlaybook = (p: Pick<Playbook, 'fromStatusId' | 'toStatusId'>): boolean =>
  p.fromStatusId === p.toStatusId;

/**
 * The move a row is for, as a phrase: `todo to in progress`, or `review
 * conflicts` for the conflict playbook. What a control's accessible name is
 * built from, so two rows on one page never share one.
 */
export function transitionLabel(p: Pick<Playbook, 'fromStatusId' | 'toStatusId' | 'fromStatusName' | 'toStatusName'>): string {
  return isConflictPlaybook(p) ? `${p.fromStatusName} conflicts` : `${p.fromStatusName} to ${p.toStatusName}`;
}
