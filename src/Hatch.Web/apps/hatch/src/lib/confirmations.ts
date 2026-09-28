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
import type { CloseOffer } from './closeSubtree';
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

/**
 * One issue a close offer moved with the card, and how to put it back: where
 * it stood before the drop, and the column the offer put it in, so the server
 * refuses to pull it out of a column somebody has since moved it to.
 */
export interface CascadeEntry {
  key: string;
  restore: IssueMoveRequest;
  /** Its rank before the drop. Not sent - the server owns the number - but it
      is what puts the entries back in the order they were in. */
  rank: number;
}

export interface MovedConfirmation extends Chicklet {
  kind: 'moved';
  from: ColumnRef;
  to: ColumnRef;
  /** The undo, built in full at drop time from the board as it stood: the
      column it came from, the neighbours it had there, and `fromStatusId` -
      the column it was dropped into - so the server can refuse to take a card
      out of a column somebody else has since moved it to. */
  restore: IssueMoveRequest;
  /** What an accepted close offer moved along with the card, and which Undo
      takes back after it. Empty for a drop that closed nothing else. */
  cascade: CascadeEntry[];
  state: MoveState;
  /** What the chicklet says once it has settled: what happened, or why not.
      Null while the move line is what it says. */
  note: string | null;
}

export type Confirmation = FiledConfirmation | MovedConfirmation;

/** What raising a move takes: everything but what the stack owns. */
export type MoveRaise = Pick<MovedConfirmation, 'issueKey' | 'title' | 'from' | 'to' | 'restore'>;

const unsettled = { cascade: [] as CascadeEntry[], state: 'moved' as MoveState, note: null };

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
  return [{ ...move, ...unsettled, id, kind: 'moved' }, ...stack];
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

/**
 * What the operator's accepted close offer moved, attached to the chicklet for
 * the card it was asked about: the newest one, because the board raises that
 * chicklet just before it asks.
 *
 * Merged by key rather than replaced. Pressing the dialog's confirm again after
 * a partial refusal is safe - what already moved comes back unchanged - and a
 * replacement would forget the first press's keys. A key with no chicklet
 * (closed since, or never raised) attaches to nothing.
 */
export function withCascade(stack: Confirmation[], issueKey: string, entries: CascadeEntry[]): Confirmation[] {
  const at = stack.findIndex((c) => c.kind === 'moved' && c.issueKey === issueKey);
  const target = stack[at];
  if (target?.kind !== 'moved') return stack;

  const held = new Set(target.cascade.map((e) => e.key));
  const added = entries.filter((e) => !held.has(e.key));
  if (added.length === 0) return stack;

  return stack.map((c, i) => (i === at ? { ...target, cascade: [...target.cascade, ...added] } : c));
}

/**
 * What a close offer is about to take out of each column, as the requests that
 * would put it back - read off the board before the drop, from the same board
 * the drop was read from.
 */
export function cascadeEntries(board: Board, offer: CloseOffer): CascadeEntry[] {
  return offer.cards.flatMap((card) => {
    const back = restorePoint(board.issues, card.key);
    return back ? [{ key: card.key, rank: card.rank, restore: { ...back, fromStatusId: offer.column.id } }] : [];
  });
}

/**
 * The order to put a cascade back in: column by column, top to bottom.
 *
 * The order matters because the server tries a request's `beforeKey` first and
 * then its `afterKey`, and falls back to the bottom of the column. Top-down,
 * each card's `afterKey` is already home when it lands, so neighbours that were
 * adjacent before the drop return adjacent and in their old order - and a
 * `beforeKey` that is still away costs nothing, because `afterKey` places it.
 */
export function cascadeOrder(entries: CascadeEntry[]): CascadeEntry[] {
  return [...entries].sort((a, b) => (a.restore.statusId ?? 0) - (b.restore.statusId ?? 0) || a.rank - b.rank);
}

/**
 * The sentence for a chicklet whose card is back, and whatever went back with
 * it - naming each issue that stayed, and why.
 *
 * @param stayed One sentence per issue that could not go back; the server's own
 * where it refused, which already names the issue and where it is now.
 */
export function undoneNote(c: MovedConfirmation, stayed: string[]): string {
  const back = c.cascade.length - stayed.length;
  const under = back > 0 ? `, with ${back} under it` : '';
  const left = stayed.length > 0 ? `. Left where they are: ${stayed.join('; ')}` : '';
  return `moved back to ${c.from.name}${under}${left}`;
}

/** The clause a move line gains when an accepted close offer took work with the
    card. Empty when none did. */
export function cascadeClause(c: MovedConfirmation): string {
  return c.cascade.length > 0 ? `, and ${c.cascade.length} under it closed` : '';
}
