/* Reading a playbook proposal: the banner sentence and the before/after table's rows. */

import { transitionLabel } from './playbooks';
import type { PlaybookProposal } from '../types';

/** The count of *proposals* (not changes) carrying at least one out-of-date
    change - the banner's own unit, matching the count it appends next to
    itself. */
export function outOfDateProposalCount(proposals: PlaybookProposal[]): number {
  return proposals.filter((p) => p.changes.some((c) => c.outOfDate)).length;
}

/** The banner's sentence: how many proposals are awaiting a decision, and how
    many of those carry an out-of-date change. */
export function proposalsBannerSentence(proposals: PlaybookProposal[]): string {
  const n = proposals.length;
  const sentence = `${n} proposed change${n === 1 ? '' : 's'} awaiting a decision`;
  const outOfDate = outOfDateProposalCount(proposals);
  return outOfDate > 0 ? `${sentence}, ${outOfDate} out of date` : sentence;
}

/** One row of the before/after table: which field, what it reads as, and
    whether this proposal's change touches it. */
export interface ProposalFieldRow {
  field: string;
  label: string;
  changed: boolean;
}

const PROPOSAL_FIELDS: { field: string; label: string }[] = [
  { field: 'model', label: 'Model' },
  { field: 'effort', label: 'Effort' },
  { field: 'fromStatusId', label: 'From' },
  { field: 'toStatusId', label: 'To' },
  { field: 'types', label: 'Types' },
  { field: 'shape', label: 'Shape' },
  { field: 'budget', label: 'Budget' },
  { field: 'briefLimit', label: 'Brief limit' },
  { field: 'deniedTools', label: 'Denied tools' },
  { field: 'isCheck', label: 'Is check' },
  { field: 'projectId', label: 'Project' },
];

/** The before/after table's rows, in HA-1642's fixed order - `prompt` is
    excluded, since that is PromptDiff's row, not the table's. */
export function proposalFieldRows(changedFields: string[]): ProposalFieldRow[] {
  return PROPOSAL_FIELDS.map(({ field, label }) => ({ field, label, changed: changedFields.includes(field) }));
}

/** Whether the prompt itself is part of a proposal's change - the card's cue
    to render PromptDiff at all. */
export function promptChanged(changedFields: string[]): boolean {
  return changedFields.includes('prompt');
}

/** Why the *Approve all* button is disabled, or null when nothing stops it.
    Scans every proposal's every change, in order, and names the first one
    that has gone out of date - matching the wording ApproveAll's own 409
    already uses. */
export function approveAllDisabledHint(proposals: PlaybookProposal[]): string | null {
  for (const proposal of proposals) {
    for (const change of proposal.changes) {
      if (change.outOfDate) {
        return `${transitionLabel(change.before)} was ${change.outOfDateReason} - reject it or have it resubmitted`;
      }
    }
  }
  return null;
}

/** The toast *Approve all* leaves behind: how many rows its proposals touched. */
export function approveAllSuccessSentence(changed: number): string {
  return `${changed} change${changed === 1 ? '' : 's'} applied`;
}

/** The toast *Reject all* leaves behind: how many proposals it decided. */
export function rejectAllSuccessSentence(rejected: number): string {
  return `${rejected} proposal${rejected === 1 ? '' : 's'} rejected`;
}
