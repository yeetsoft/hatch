/* Which columns are the board, and what counts as work that has stopped.

   Two one-line questions, in a module of their own for the reason lib/place.ts
   and lib/closeSubtree.ts are: they are asked on four screens, and four copies
   of `!s.isDeferred` is four places to forget it. The server holds the same two
   answers in Columns.cs and reads them the same way - a board measured off the
   columns it actually draws, and a ticket that has stopped counting as stopped
   however it got there.

   Nothing here is a transition rule. Every column is still a legal destination
   for every issue (docs/hatch.md, "Non-goals"); this only says which of them
   the board is made of. */

import type { Status } from '../types';

/**
 * The columns the board draws, in the order it draws them.
 *
 * A deferred column is left out because a column on the board is a drop target,
 * and a siding is exactly the place work must not drift into by being dragged
 * one column too far. The way into one is the issue page's status bar, which is
 * a press somebody has to mean.
 *
 * The API sends every column on every read regardless - the issue page needs
 * the deferred ones in order to offer them - so this is the only thing standing
 * between the two, and every board-shaped list goes through it.
 */
export const boardColumns = (statuses: Status[]): Status[] => statuses.filter((s) => !s.isDeferred);

/**
 * Whether work sitting in this column has stopped: shipped, or shelved.
 *
 * The two are not the same fact and are stored apart - a rollup counts one and
 * discards the other - but wherever the question is "is anybody still expecting
 * this to move", they answer it together. A due date on a parked ticket is not
 * a date anybody is late for, and a parked child is not open work to be swept
 * into its parent's column.
 */
export const isSettled = (status: Status | undefined): boolean =>
  status ? status.isTerminal || status.isDeferred : false;

/** The board's columns, and the shelf beside them - the two-part list every
    status bar and status picker draws: the lanes in board order, then the
    deferred columns set apart. */
export function statusChoices(statuses: Status[]): { lanes: Status[]; shelf: Status[] } {
  return { lanes: boardColumns(statuses), shelf: statuses.filter((s) => s.isDeferred) };
}

/**
 * Where the keyboard lands when a picker opens on this column: one lane
 * along the board, in the direction that makes Enter, Enter walk it forward.
 *
 * From the last lane there is nowhere further on, so it lands one lane back
 * instead - and from a lane that is not on the board at all (deferred, or a
 * column the picker's caller no longer recognises), it lands on the first
 * lane, since there is no "next" to reason from. Null only when the board has
 * no other lane to offer - a single-column board, or one with none at all.
 */
export function landingFocus(statuses: Status[], statusId: number): number | null {
  const { lanes } = statusChoices(statuses);
  const at = lanes.findIndex((s) => s.id === statusId);

  if (at < 0) return lanes[0]?.id ?? null;
  if (at < lanes.length - 1) return lanes[at + 1].id;
  return lanes[at - 1]?.id ?? null;
}
