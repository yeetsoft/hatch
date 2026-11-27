/* The pre-approval levels, as data: shared by the control that draws them and
   the console that offers them, so neither copies the other's table. */

export type PreApproval = 'none' | 'express' | 'preapproved';

export interface PreApprovalLevel {
  name: PreApproval;
  word: string;
  glyph: string;
  class: string;
  title: string;
}

/** Why a step is disabled for permission rather than for being current - the
    exact sentence `IssuePreApprovalController.SetAsync` refuses the write
    with, so a press this band offers never meets a 403 the server already
    knew about. */
export const PERMISSION_TITLE =
  "Only an owner or approver of the project may set pre-approval - it is the approve press given in advance";

/** Every level the picker offers, in the order the band draws them - this is
    the one place that names a level. */
export const PRE_APPROVAL_LEVELS: PreApprovalLevel[] = [
  {
    name: 'none',
    word: 'None',
    glyph: '',
    class: '',
    title: 'No approval banked: this issue asks for its own press at every gate, the same as any other.',
  },
  {
    name: 'express',
    word: 'Express',
    glyph: '»',
    class: 'express',
    title:
      'Carried past every column marked Express skips, with no session, whenever it holds no open question. Changes no gate but that one.',
  },
  {
    name: 'preapproved',
    word: 'Pre-approved',
    glyph: '✓',
    class: 'preapproved',
    title:
      'The approve press given in advance: carried past every column marked Express skips, and past the approval gate itself, whenever it holds no open question.',
  },
];

/** A pre-approval level as the word `PreApprovalControl`'s pill wears. Shared
    by the band that sets it and anything that only reads the level, so
    neither names a word the other does not. */
export function preApprovalWord(level: PreApproval): string {
  return PRE_APPROVAL_LEVELS.find((l) => l.name === level)?.word ?? level;
}

/** Whether a level is out of this person's reach: setting preapproved, and
    lowering out of it, are the approver's act. The own level is never locked,
    since it is already marked and cannot be pressed for being current. */
export function approvalLocked(level: PreApproval, own: PreApproval, canApprove: boolean): boolean {
  return level !== own && !canApprove && (level === 'preapproved' || own === 'preapproved');
}
