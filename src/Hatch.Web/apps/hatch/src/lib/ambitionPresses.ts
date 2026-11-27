import type { AmbitionRow, Me } from '../types';
import type { PreApproval } from './preApproval';
import { preApprovalWord } from './preApproval';
import { moneyPhrase } from './workLog';

/** Which press a row offers, or null for neither. */
export type AmbitionPress = 'shape' | 'start';

/**
 * Shape it for a draft whose project has a row that shapes ambitions; Start
 * it for a ready row with an implementation column to start into; neither
 * otherwise. Drawn for a person only - a key is refused both moves for the
 * reason `AmbitionsContent.onNew` already guards against: a wall-off Hatch
 * reads a browser as the local person, so this is a guard only, never a
 * refusal a key would actually meet.
 */
export function pressFor(row: AmbitionRow, me: Me | null): AmbitionPress | null {
  if (me?.kind === 'key') return null;
  if (row.stage === 'draft' && row.shapingStatusId !== null) return 'shape';
  if (row.stage === 'ready' && row.startStatusId !== null) return 'start';
  return null;
}

/** The four lines the Start it dialog asks over before moving the ambition in. */
export function startSentences(row: AmbitionRow, preApproval: PreApproval): string[] {
  const capabilities = `A pass will check ${row.capabilities} ${row.capabilities === 1 ? 'capability' : 'capabilities'}.`;
  const epics = `${row.limit} ${row.limit === 1 ? 'epic runs' : 'epics run'} at once.`;
  const budget = row.budget.budgetUsd === null ? 'No budget is set.' : `Budget: ${moneyPhrase(row.budget.budgetUsd)}.`;
  const approval = `Pre-approval: ${preApprovalWord(preApproval)}.`;

  return [capabilities, epics, budget, approval];
}
