/* What the Latest change section on an ambition's page says, apart from the
   component - the same split lib/budget.ts and lib/closeSubtree.ts make, and
   for the same reason: this workspace has no DOM test setup, so what can be
   wrong lives in a function with a test beside it.

   The section draws no sums of its own. Every word arrives from
   GET /api/issues/{key}/changes; what is here is which badge an entry wears,
   what an outcome and a decision read as, whether the two presses show for a
   given outcome, the shelve dialog's sentence, and the write. */

import { decideChangeOutcome } from '../api/client';
import { isSettled } from './columns';
import { message } from './errors';
import type { AmbitionChange, ChangeDecision, ChangeEntry, ChangeOutcome, Status } from '../types';

/** The latest change, or null where the ambition has never had one - `changes`
    is newest first, so this is never more than picking the first. */
export function latestChange(changes: AmbitionChange[] | null): AmbitionChange | null {
  return changes && changes.length > 0 ? changes[0] : null;
}

/** The badge an entry's kind wears. */
export function entryTone(kind: ChangeEntry['kind']): 'success' | 'primary' | 'danger' {
  if (kind === 'added') return 'success';
  if (kind === 'changed') return 'primary';
  return 'danger';
}

/** The badge's word. */
export function entryLabel(kind: ChangeEntry['kind']): string {
  if (kind === 'added') return 'Added';
  if (kind === 'changed') return 'Changed';
  return 'Removed';
}

/** The word an epic's outcome reads as. */
export function outcomeWord(outcome: ChangeOutcome['outcome']): string {
  if (outcome === 'carries_on') return 'Carries on';
  if (outcome === 'not_touched') return 'Not touched';
  return 'Serves nothing now';
}

/** The word a decision reads as, past tense - "Shelved" or "Kept running". */
export function decisionWord(decision: ChangeDecision['decision']): string {
  return decision === 'shelve' ? 'Shelved' : 'Kept running';
}

/** Whether the column named on an outcome is one the board still counts as
    open: a status by that name, neither terminal nor deferred. A name the
    board no longer carries is not open, the same as closeSubtree's own
    `stopped` filter treats an unresolvable column. */
export function epicIsOpen(epicColumn: string, statuses: Status[]): boolean {
  const status = statuses.find((s) => s.name === epicColumn);
  return status ? !isSettled(status) : false;
}

/** Whether a line shows the two decision presses: an outcome of serves
    nothing, an epic still open, and nobody has decided it yet. The caller
    checks the decision first, so this only has to ask about the outcome and
    whether the epic is still open. */
export function showsPresses(outcome: ChangeOutcome, statuses: Status[]): boolean {
  return outcome.outcome === 'serves_nothing' && outcome.decision === null && epicIsOpen(outcome.epicColumn, statuses);
}

/** The sentence the shelve dialog leads with, naming how many open issues move
    with the epic - the count the caller read off `closeSubtree`, the same
    walk the server's shelve does. Zero says there is nothing to carry along,
    rather than naming a column nothing is moving to. */
export function shelveSentence(epicKey: string, openCount: number, deferredColumnName: string): string {
  if (openCount === 0) return `Shelve ${epicKey}? Nothing under it is open.`;

  const them = openCount === 1 ? 'issue' : 'issues';
  return `Shelve ${epicKey}? It and the ${openCount} open ${them} under it move to ${deferredColumnName}.`;
}

/** Records a decision through the API. Answers null when it went through, or
    the refusal in the server's own words - never throws, the same shape
    writeBudget answers in. */
export async function decideOutcome(
  key: string,
  outcomeId: number,
  decision: ChangeDecision['decision'],
): Promise<string | null> {
  try {
    await decideChangeOutcome(key, outcomeId, { decision });
    return null;
  } catch (err) {
    return message(err);
  }
}
