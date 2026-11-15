/* What the dream cloud asks, and what it says afterwards.

   Dropping a card on the cloud remands it to a dream: the card and the open
   work under it go to a deferred column, and a dream is posted from the card.
   That is a lot to hang on one gesture, so a drop on the cloud commits nothing -
   it opens a dialog, and this module is the whole of what the dialog decides:
   whether there is a cloud to draw, which column, which cards, and in what
   words the chicklet reports it.

   It is a module rather than a block in the page for the reason
   lib/closeSubtree.ts gives: there is no DOM in the web test run, and getting a
   list of keys slightly wrong has no visible symptom. The same walk finds the
   descendants - `closeSubtree` - so the dialog lists exactly what the server
   will move.

   Nothing here is a transition rule. Any card may be dragged to the cloud, as
   any card may be dragged to a column. */

import { closeSubtree } from './closeSubtree';
import { scopedPath } from './scope';
import type { Board, DreamRemandRequest, DreamRemandResult, IssueCard, Status } from '../types';

/** The deferred columns, in board order - the ones a remand can shelve into. */
const shelves = (statuses: Status[]): Status[] =>
  statuses.filter((s) => s.isDeferred).sort((a, b) => a.sortOrder - b.sortOrder || a.id - b.id);

/**
 * Whether the board draws a cloud at all: only when there is a deferred column
 * for a remand to go to. A board with none has nowhere to shelve work, and an
 * offer that could only be refused is not drawn.
 */
export const showCloud = (statuses: Status[]): boolean => shelves(statuses).length > 0;

export interface RemandOffer {
  /** The card dropped on the cloud. */
  key: string;
  /** Its title, which is the first line of the dream the server posts. */
  title: string;
  /** The deferred columns it could go to, in board order. More than one is
      what puts a chooser in the dialog. */
  columns: Status[];
  /** The one chosen. */
  statusId: number;
  /** The open descendants that would go too, in board order. Empty when every
      one has already stopped - the card itself is still remanded. */
  cards: IssueCard[];
}

/**
 * The question to ask when a card is dropped on the cloud, or null when there
 * is none: the card is not on the board, or no column is deferred.
 *
 * @param statusId The column to default to, when it is a deferred one; the
 * first deferred column otherwise.
 */
export function remandOffer(board: Board, key: string, statusId?: number): RemandOffer | null {
  const card = board.issues.find((i) => i.key === key);
  const columns = shelves(board.statuses);
  if (!card || columns.length === 0) return null;

  const chosen = columns.find((c) => c.id === statusId) ?? columns[0];
  return {
    key,
    title: card.title,
    columns,
    statusId: chosen.id,
    cards: closeSubtree(board.issues, key, board.statuses),
  };
}

/** What `POST /api/issues/{key}/remand` is sent: the column, and the keys the
    operator was shown - never a flag the server would expand at write time. */
export const remandRequest = (offer: RemandOffer): DreamRemandRequest => ({
  statusId: offer.statusId,
  keys: offer.cards.map((c) => c.key),
});

/** The dialog's heading. */
export const remandTitle = (offer: RemandOffer): string => `Remand ${offer.key} to a dream?`;

/** The column the offer would go to. */
export const remandColumn = (offer: RemandOffer): Status =>
  offer.columns.find((c) => c.id === offer.statusId) ?? offer.columns[0];

/** Where the chicklet's link goes: the Dreams page, narrowed to the dream the
    remand posted by the issue it was remanded from. */
export const dreamHref = (rootKey: string): string => `/dreams?remandedFrom=${encodeURIComponent(rootKey)}`;

/** `dreamHref` with the scope kept. `scopedPath` takes a path with no query of
    its own, and this one has one, so the scope joins it with `&`. */
export function scopedDreamHref(href: string, keys: string[]): string {
  const [path, query] = href.split('?');
  const scoped = scopedPath(path, keys);
  if (!query) return scoped;
  return `${scoped}${scoped.includes('?') ? '&' : '?'}${query}`;
}

/**
 * What the chicklet says. The count is the issues that moved under the root,
 * which is what `changed` holds besides it - an issue already in the column, or
 * already stopped, is `unchanged` and was not remanded by this.
 */
export function remandedSentence(rootKey: string, result: DreamRemandResult): string {
  const under = result.changed.filter((k) => k !== rootKey).length;
  if (under === 0) return `${rootKey} remanded to a dream.`;
  return `${rootKey} remanded to a dream, with ${under} ${under === 1 ? 'issue' : 'issues'} under it.`;
}
