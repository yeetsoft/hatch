/* What the peek remembers about a card's priority and pre-approval, so it can
   tell when the card has moved on from a level the peek itself asked for.

   The peek draws the answer to its own press in preference to the card, so the
   pill answers at once. That answer is stale once the card changes under it -
   the board reloaded, whether after the press or after the console set the
   level - and the card is then the newer word. Pure and tested in node, since
   the peek's state cannot be driven without a DOM. */

import type { IssueCard } from '../types';

type Levels = Pick<
  IssueCard,
  'priority' | 'priorityOwn' | 'priorityFrom' | 'preApproval' | 'preApprovalOwn' | 'preApprovalFrom'
>;

/** What the card says about its priority: the level in effect, its own, and
    where the effective one came from. Null with no card. */
export function priorityStamp(card: Levels | null): string | null {
  return card ? `${card.priority}|${card.priorityOwn}|${card.priorityFrom}` : null;
}

/** The same, for pre-approval. */
export function preApprovalStamp(card: Levels | null): string | null {
  return card ? `${card.preApproval}|${card.preApprovalOwn}|${card.preApprovalFrom}` : null;
}
