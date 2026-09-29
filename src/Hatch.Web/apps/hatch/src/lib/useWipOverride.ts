/* What happens after the operator answers the dialog a full WIP section
   raises. Copies useCloseSubtree.ts's shape for the same reason it exists:
   two screens ask this question - the board on a drop, the issue page on its
   status bar - and one copy of the retry in each is two error paths that
   drift.

   Unlike useCloseSubtree, which always re-sends the same bulk edit, each call
   here retries a different request - a board drop and an issue-page press
   post to different routes with different shapes - so the hook holds the
   caller's own retry closure alongside what is being asked. */

import { useCallback, useState } from 'react';
import { message } from './errors';
import type { WipRefusal } from '../types';

export interface WipOverride {
  /** The refusal being asked about, or null when nothing is. */
  asking: { key: string; refusal: WipRefusal } | null;
  /** The retry is in flight: the confirm stops taking presses and says so. */
  busy: boolean;
  /** What the retry was refused with, as a sentence. Null while nothing has. */
  error: string | null;
  ask: (key: string, refusal: WipRefusal, retry: () => Promise<void>) => void;
  confirm: () => void;
  close: () => void;
}

/**
 * @param onLeave Called when the operator presses Leave it or dismisses the
 * dialog - the board's `reload`, so the board catches up with whatever put
 * the card back. The issue page passes nothing: its own catch has already
 * reloaded before asking.
 */
export function useWipOverride(onLeave?: () => void): WipOverride {
  const [asking, setAsking] = useState<{ key: string; refusal: WipRefusal } | null>(null);
  const [retry, setRetry] = useState<(() => Promise<void>) | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const ask = useCallback((key: string, refusal: WipRefusal, next: () => Promise<void>) => {
    setAsking({ key, refusal });
    // A function value stored in state is called by useState's setter if
    // passed directly, so it is wrapped to store the closure itself.
    setRetry(() => next);
    setBusy(false);
    setError(null);
  }, []);

  const close = useCallback(() => {
    setAsking(null);
    setRetry(null);
    setBusy(false);
    setError(null);
    onLeave?.();
  }, [onLeave]);

  const confirm = useCallback(() => {
    if (!retry || busy) return;

    setBusy(true);
    setError(null);

    void (async () => {
      try {
        await retry();
        setAsking(null);
        setRetry(null);
        setBusy(false);
      } catch (err) {
        setError(message(err));
        setBusy(false);
      }
    })();
  }, [retry, busy]);

  return { asking, busy, error, ask, confirm, close };
}
