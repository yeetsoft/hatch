import { agoPhrase } from './claim';
import type { Comment, IssueClaim } from '../types';

/** How often the issue page re-reads while a message is waiting. Short, because
    the person who just pressed Send is watching for the answer: a state that
    took the board's thirty seconds to change would read as a message that fell
    into a black hole, which is the one thing this exists to rule out. */
export const WATCH_MS = 5 * 1000;

/** Where a message to the agent stands. `read` means it was put into a
    session's context - nothing can prove the model acted on it. */
export type MessageState = 'read' | 'waiting' | 'held';

export interface MessageStatus {
  state: MessageState;
  /** The state in words, which is how it is drawn: a colour is not a sentence. */
  words: string;
}

const isMessage = (comment: Comment) => comment.kind === 'message';

const unread = (comment: Comment) => isMessage(comment) && comment.deliveredAt === null;

/**
 * Where one comment stands, or null for anything that is not a message - a note
 * shows no read state at all, because nobody was ever going to read it to a
 * session.
 *
 * The claim is what tells the two unread states apart. Hatch hands out a live
 * claim or null, never an expired one, so a claim here is somebody working the
 * issue: an unread message is *waiting* for their next step. With none it is
 * *held*, and the words say where it goes instead of leaving it to look lost.
 */
export function messageState(
  comment: Comment,
  claim: IssueClaim | null,
  issueKey: string,
  now: Date,
): MessageStatus | null {
  if (!isMessage(comment)) return null;

  if (comment.deliveredAt !== null) {
    const by = comment.deliveredTo ? ` by ${comment.deliveredTo}` : '';
    return { state: 'read', words: `read ${agoPhrase(comment.deliveredAt, now)}${by}` };
  }

  return claim
    ? { state: 'waiting', words: 'not read yet' }
    : { state: 'held', words: `not read — nobody is working ${issueKey}; the next session on it is told` };
}

/**
 * What the Claim panel lists under its box: every message nobody has read, and
 * every one read since this claim was taken - so the panel shows what this
 * session has been told and what it has yet to be, and not the whole history
 * of a ticket that has had many sessions. Oldest first, as the thread is.
 *
 * Empty with no claim: the panel draws nothing then, and a held message is
 * shown in the thread.
 */
export function claimMessages(comments: Comment[], claim: IssueClaim | null): Comment[] {
  if (!claim) return [];

  const since = Date.parse(claim.claimedAt);

  return comments
    .filter((comment) => {
      if (!isMessage(comment)) return false;
      if (comment.deliveredAt === null) return true;

      // A read time that cannot be read is dropped rather than passed: it
      // cannot be said to belong to this claim.
      return Date.parse(comment.deliveredAt) >= since;
    })
    .sort((a, b) => Date.parse(a.createdAt) - Date.parse(b.createdAt) || a.id - b.id);
}

/**
 * Whether the page should be re-reading: a session holds the issue and some
 * message to it is unread. False the moment nothing is waiting or the claim
 * ends - a held message changes only when a session starts, which is not
 * something a five-second poll would make sooner.
 */
export function watching(comments: Comment[], claim: IssueClaim | null): boolean {
  return claim !== null && comments.some(unread);
}
