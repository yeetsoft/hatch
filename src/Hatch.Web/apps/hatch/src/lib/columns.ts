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

/**
 * Whether a question on this issue has stopped being something a person
 * owes an answer to: true once the issue has shipped, in a terminal
 * column.
 *
 * Deliberately narrower than isSettled - a deferred issue is parked, not
 * done, and a question on it still asks.
 */
export const isObviated = (status: Status | undefined): boolean =>
  status ? status.isTerminal : false;

/** The board's columns, and the shelf beside them - the two-part list every
    status bar and status picker draws: the lanes in board order, then the
    deferred columns set apart. */
export function statusChoices(statuses: Status[]): { lanes: Status[]; shelf: Status[] } {
  return { lanes: boardColumns(statuses), shelf: statuses.filter((s) => s.isDeferred) };
}

/**
 * The column immediately to the right, or null at the end of the board.
 *
 * Deferred and terminal columns have nothing after them: work is never
 * advanced into a siding, and a shipped or shelved column is where a board
 * walk stops.
 */
function advance(statuses: Status[], from: Status): Status | null {
  if (from.isTerminal || from.isDeferred) return null;
  const board = boardColumns(statuses);
  const at = board.findIndex((s) => s.id === from.id);
  return at >= 0 && at + 1 < board.length ? board[at + 1] : null;
}

/**
 * The last stop before shipped: the column immediately left of the first
 * terminal one, or the rightmost column on a board with no terminal column
 * at all - stepping back further over any protected columns standing in
 * that run, since a protected column is one a person gated, not one work
 * drifts into on its way to review.
 *
 * Measured rather than named, mirroring the server's Columns.AwaitingReview -
 * an operator renames columns, and a hardcoded "review" would be a rule that
 * quietly stopped applying.
 */
export function awaitingReview(statuses: Status[]): Status | null {
  const board = boardColumns(statuses);
  const terminal = board.findIndex((s) => s.isTerminal);
  let at = terminal < 0 ? board.length - 1 : terminal - 1;
  while (at >= 0 && board[at].isProtected) at--;
  return at >= 0 ? board[at] : null;
}

/**
 * The column where code gets written: the one a person ticked
 * isImplementation on. Where none is ticked, the one whose own next move is
 * into the awaiting-review column. Null only on an unticked board too short
 * to have one.
 *
 * Mirrors the server's Columns.Implementation for the same reason
 * awaitingReview does - a tick on a deferred or terminal column is ignored,
 * since the board then answers as if nothing were ticked.
 */
export function implementation(statuses: Status[]): Status | null {
  const ticked = statuses.find((s) => s.isImplementation && !s.isDeferred && !s.isTerminal);
  if (ticked) return ticked;
  const review = awaitingReview(statuses);
  return review ? (statuses.find((s) => advance(statuses, s)?.id === review.id) ?? null) : null;
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
