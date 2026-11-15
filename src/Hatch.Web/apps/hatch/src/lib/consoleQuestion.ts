/* The question the console asks inline after an issue is moved to a column
   where work stops: whether what is under it stops too.

   The same question CloseSubtreeDialog asks, in the same words for the same
   two cases - close under a terminal column, shelve under a deferred one - but
   as one line and two rows, since the console is a prompt and not a dialog.
   Whether to ask at all is lib/closeSubtree.ts's `closeOffer`. Pure and tested
   in node. */

import type { CloseOffer } from './closeSubtree';

export interface QuestionRow {
  id: 'yes' | 'no';
  name: string;
}

/** The question, with how many there are. */
export function closeQuestion(offer: CloseOffer): string {
  const verb = offer.column.isDeferred ? 'Shelve' : 'Close';
  const count = offer.cards.length;
  return count === 1 ? `${verb} its open child too?` : `${verb} its ${count} open children too?`;
}

/** The two answers, the one that acts first. */
export function questionRows(): QuestionRow[] {
  return [
    { id: 'yes', name: 'Yes' },
    { id: 'no', name: 'No' },
  ];
}
