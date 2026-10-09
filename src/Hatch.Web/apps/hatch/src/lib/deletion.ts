/* Whether a delete goes ahead, and what the screen should say about it.

   The issue page and the board's peek both throw an issue away, and both ask
   first with the browser's own confirm. This module is the whole of deciding
   what that means - ask, then send or don't, then what to show - so the
   sentence asked cannot differ between the two surfaces, and the decision can
   be tested without a DOM to press a button in.

   The confirm and the request are handed in rather than reached for, so
   nothing here knows about `window` or the API client. */

import { message } from './errors';

/** The one sentence both surfaces ask before a delete. */
export const deleteQuestion = (key: string) =>
  `Delete ${key}? Its comments and its history go with it.`;

export type DeleteOutcome =
  | { outcome: 'cancelled' }
  | { outcome: 'deleted' }
  | { outcome: 'refused'; error: string };

/**
 * Ask, and only if the answer is yes, send. A refusal is an answer rather than
 * a throw, carrying the server's sentence for the caller to put on screen.
 */
export async function askToDelete(
  key: string,
  ask: (question: string) => boolean,
  remove: (key: string) => Promise<void>,
): Promise<DeleteOutcome> {
  if (!ask(deleteQuestion(key))) return { outcome: 'cancelled' };
  try {
    await remove(key);
    return { outcome: 'deleted' };
  } catch (err) {
    return { outcome: 'refused', error: message(err) };
  }
}
