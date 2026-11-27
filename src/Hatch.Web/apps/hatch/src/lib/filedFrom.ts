/* What an issue may be said to have been filed from.
 *
 * Provenance only - it holds nothing, orders nothing and blocks nothing, so
 * unlike parentCandidates and dependencyCandidates there is no project or
 * type restriction, and no ancestor/descendant walk. The server
 * (ResolveFiledFromAsync, IssuesController) refuses only the issue itself;
 * everything else, including another project, is legal. */

import type { PickedRow } from './pickerLookup';
import type { IssueCard } from '../types';

/**
 * The cards `self` may be said to have been filed from: every card except
 * itself.
 */
export function filedFromCandidates(cards: IssueCard[], self: string): IssueCard[] {
  return cards.filter((c) => c.key !== self);
}

/**
 * The same refusal, over a row the lookup found rather than a card the page
 * holds: everything but `self`. Needed because an issue filed from one closed
 * long enough to be off the board is otherwise only caught by the server's
 * own refusal.
 */
export function filedFromLookupFilter(self: string): (row: PickedRow) => boolean {
  return (row) => row.key !== self;
}
