import type { AmbitionChanges } from '../types';

/* The rules of looking at the Ambitions page: which instant a read asks from,
   and when the visit is recorded. Plain functions, so they are tested without a
   DOM; AmbitionsPage only wires them to a ref and an effect. */

/** What the first drawn read of this mount ran from, kept so every later read
    asks from the same instant, whatever visit has been recorded since. */
export interface HeldVisit {
  since: string;
  basis: AmbitionChanges['basis'];
}

/** The `since` to send: none before a read has been held, which leaves the
    server to find it. */
export const sinceToSend = (held: HeldVisit | null): string | undefined => held?.since;

/**
 * Whether to record the visit now. Only for a draw made for the scope in force,
 * only where what changed was read (a failed read is not a look), and only once
 * in a mount.
 */
export function shouldRecord({
  current,
  changes,
  recorded,
}: {
  current: boolean;
  changes: AmbitionChanges | null;
  recorded: boolean;
}): boolean {
  return current && changes !== null && !recorded;
}

/** What to keep after a draw: the first read's instant and basis, never a later one's. */
export function holdFirstRead(held: HeldVisit | null, changes: AmbitionChanges): HeldVisit {
  return held ?? { since: changes.since, basis: changes.basis };
}

/** The basis to draw. A later read sends the held `since` and so comes back
    `given`; the held basis is shown instead, so a first-visit note survives. */
export function shownBasis(held: HeldVisit | null, changes: AmbitionChanges): AmbitionChanges['basis'] {
  return held?.basis ?? changes.basis;
}
