/**
 * The stack of chicklets in the corner, as data: what was filed in this tab,
 * and what was dragged to another column and can still be taken back.
 *
 * A confirmation is about what *this* visit did, so the stack is state in the
 * page and nothing else: nothing here reads or writes localStorage or
 * sessionStorage, which is what makes a reload - and a second tab, and a
 * restarted browser - start on an empty corner without a line of code spent
 * clearing anything.
 */

import { restorePoint } from './place';
import type { Board, IssueMoveRequest } from '../types';

/** A column named on a chicklet. The name is kept, because the chicklet is read
    after the board may have renamed or reordered what it points at. */
export interface ColumnRef {
  id: number;
  name: string;
}

interface Chicklet {
  /** Unique per raise, so React has a stable key and filing under a key that
      is already on screen cannot collide with the older chicklet. */
  id: number;
  issueKey: string;
  title: string;
}

export interface FiledConfirmation extends Chicklet {
  kind: 'filed';
}

/**
 * Where a move chicklet is in its life. Only `moved` and `failed` can be
 * pressed: `undoing` is a request in flight, and `undone` and `refused` are
 * over.
 */
export type MoveState = 'moved' | 'undoing' | 'undone' | 'refused' | 'failed';

export interface MovedConfirmation extends Chicklet {
  kind: 'moved';
  from: ColumnRef;
  to: ColumnRef;
  /** The undo, built in full at drop time from the board as it stood: the
      column it came from, the neighbours it had there, and `fromStatusId` -
      the column it was dropped into - so the server can refuse to take a card
      out of a column somebody else has since moved it to. */
  restore: IssueMoveRequest;
  state: MoveState;
  /** What the chicklet says once it has settled: what happened, or why not.
      Null while the move line is what it says. */
  note: string | null;
}

export type Confirmation = FiledConfirmation | MovedConfirmation;

/** What raising a move takes: everything but what the stack owns. */
export type MoveRaise = Pick<MovedConfirmation, 'issueKey' | 'title' | 'from' | 'to' | 'restore'>;

/**
 * A newly filed issue, on the front of the stack.
 *
 * Newest first, because the region paints in `column-reverse`: index 0 is the
 * chicklet nearest the bottom-left corner, and the older ones run upward from
 * it. Nothing is dropped and there is no cap - a stack too tall for the screen
 * is answered with a scrollbar rather than by quietly discarding the
 * confirmation somebody has not read yet.
 */
export function raise(stack: Confirmation[], issue: { key: string; title: string }, id: number): Confirmation[] {
  return [{ id, kind: 'filed', issueKey: issue.key, title: issue.title }, ...stack];
}

/** A card dropped into another column, on the front of the stack, ready to be
    taken back. */
export function raiseMove(stack: Confirmation[], move: MoveRaise, id: number): Confirmation[] {
  return [{ ...move, id, kind: 'moved', state: 'moved', note: null }, ...stack];
}

/** One chicklet closed, and every other one left exactly where it was. An id
    that is not in the stack takes nothing off it. */
export function dismiss(stack: Confirmation[], id: number): Confirmation[] {
  return stack.filter((c) => c.id !== id);
}

/** A move chicklet's new state and what it now says. A filed chicklet, and an
    id that is not in the stack, are left as they were. */
export function settle(stack: Confirmation[], id: number, state: MoveState, note: string | null): Confirmation[] {
  return stack.map((c) => (c.id === id && c.kind === 'moved' ? { ...c, state, note } : c));
}

/** Whether pressing Undo on this chicklet would do anything. A `failed` one
    can be pressed again; one in flight, or settled, cannot. */
export function canUndo(c: Confirmation): c is MovedConfirmation {
  return c.kind === 'moved' && (c.state === 'moved' || c.state === 'failed');
}

/**
 * What the keyboard takes back: the newest move chicklet that has not been
 * undone, so each press walks one further back through the stack.
 *
 * While the newest one is still in flight the answer is that there is nothing -
 * a second keystroke must wait for the first rather than reach past it and
 * undo a move the operator has not seen the end of.
 */
export function newestUndoable(stack: Confirmation[]): MovedConfirmation | null {
  const newest = stack.find((c) => c.kind === 'moved' && (c.state === 'undoing' || canUndo(c)));
  return newest && canUndo(newest) ? newest : null;
}

/**
 * The chicklet a drop earns, read off the board as it stood *before* the card
 * was moved.
 *
 * Null when it earns none: the card was only reordered within its column, or
 * it, or a column, is not on the board. A reorder is not a transition - the
 * server writes no event for one, so there is nothing to confirm or to undo.
 */
export function dropConfirmation(board: Board, key: string, toStatusId: number): MoveRaise | null {
  const card = board.issues.find((i) => i.key === key);
  const from = board.statuses.find((s) => s.id === card?.statusId);
  const to = board.statuses.find((s) => s.id === toStatusId);
  const back = restorePoint(board.issues, key);
  if (!card || !from || !to || !back || from.id === to.id) return null;

  return {
    issueKey: card.key,
    title: card.title,
    from: { id: from.id, name: from.name },
    to: { id: to.id, name: to.name },
    restore: { ...back, fromStatusId: to.id },
  };
}
