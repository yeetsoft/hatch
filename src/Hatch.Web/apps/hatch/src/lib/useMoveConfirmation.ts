/* What happens after the operator answers the confirm a contribution's move into
   a merging column raises. The shape of useWipOverride, for the same reason: the
   board on a drop or a press and the issue page on its status bar ask the same
   question, and one copy of the retry in each is two error paths that drift.

   Unlike the WIP dialog this is asked *before* the request, not after the
   server's refusal - the move is allowed, and the dialog is only a second look -
   so Leave it sends nothing and there is nothing to put back. */

import { useCallback, useState } from 'react';
import { message } from './errors';

export interface MoveConfirmation {
  /** The move being asked about, or null when nothing is. */
  asking: { key: string; sentence: string } | null;
  /** The move is in flight: the confirm stops taking presses and says so. */
  busy: boolean;
  /** What the move was refused with, as a sentence. Null while nothing has. */
  error: string | null;
  ask: (key: string, sentence: string, go: () => Promise<void>) => void;
  confirm: () => void;
  close: () => void;
}

export function useMoveConfirmation(): MoveConfirmation {
  const [asking, setAsking] = useState<{ key: string; sentence: string } | null>(null);
  const [go, setGo] = useState<(() => Promise<void>) | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const ask = useCallback((key: string, sentence: string, next: () => Promise<void>) => {
    setAsking({ key, sentence });
    // A function value stored in state is called by useState's setter if
    // passed directly, so it is wrapped to store the closure itself.
    setGo(() => next);
    setBusy(false);
    setError(null);
  }, []);

  const close = useCallback(() => {
    setAsking(null);
    setGo(null);
    setBusy(false);
    setError(null);
  }, []);

  const confirm = useCallback(() => {
    if (!go || busy) return;

    setBusy(true);
    setError(null);

    void (async () => {
      try {
        await go();
        setAsking(null);
        setGo(null);
        setBusy(false);
      } catch (err) {
        setError(message(err));
        setBusy(false);
      }
    })();
  }, [go, busy]);

  return { asking, busy, error, ask, confirm, close };
}
