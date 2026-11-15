/* What happens after a card is dropped on the dream cloud.

   The drop itself commits nothing: it opens the dialog this hook holds state
   for, and the request goes only when the operator presses Remand. So the
   whole of "a dismissed drop sends nothing" is that `confirm` is the only
   caller of the route, and `close` never reaches it - there is no DOM in the
   web test run, so that sentence is the test.

   The request names its keys. `remandOffer` read them off the board the
   operator was looking at, and they are sent as a list rather than as a flag
   the server would expand at write time, for the reason useCloseSubtree.ts
   gives. */

import { useCallback, useState } from 'react';
import { remandIssue } from '../api/client';
import { message } from './errors';
import { remandRequest } from './remand';
import type { RemandOffer } from './remand';
import type { DreamRemandResult } from '../types';

export interface Remand {
  /** The question on screen, or null when none is being asked. */
  offer: RemandOffer | null;
  /** Called with what `remandOffer` returned. Null asks nothing. */
  ask: (offer: RemandOffer | null) => void;
  /** Pick another deferred column. */
  choose: (statusId: number) => void;
  /** Leave it on the board: nothing is sent. */
  close: () => void;
  confirm: () => void;
  busy: boolean;
  /** The server's words for a refusal, or whatever else went wrong. Null while nothing has. */
  error: string | null;
}

/**
 * @param reload How the board catches up with what the remand did.
 * @param onRemanded Told once the remand has gone through and the board has
 * reloaded - the board raises its chicklet from it.
 */
export function useRemand(
  reload: () => Promise<void>,
  onRemanded?: (offer: RemandOffer, result: DreamRemandResult) => void,
): Remand {
  const [offer, setOffer] = useState<RemandOffer | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const ask = useCallback((next: RemandOffer | null) => {
    setError(null);
    setOffer(next);
  }, []);

  const choose = useCallback((statusId: number) => {
    setOffer((current) =>
      current && current.columns.some((c) => c.id === statusId) ? { ...current, statusId } : current,
    );
  }, []);

  const close = useCallback(() => {
    setOffer(null);
    setBusy(false);
    setError(null);
  }, []);

  const confirm = useCallback(() => {
    if (!offer || busy) return;

    setBusy(true);
    setError(null);

    void (async () => {
      try {
        const result = await remandIssue(offer.key, remandRequest(offer));
        await reload();
        onRemanded?.(offer, result);
        close();
      } catch (err) {
        // A refusal is the server's own sentence, shown with the dialog still
        // open: the request is one transaction, so nothing was written and the
        // operator can choose again or leave.
        setError(message(err));
      } finally {
        setBusy(false);
      }
    })();
  }, [offer, busy, reload, onRemanded, close]);

  return { offer, ask, choose, close, confirm, busy, error };
}
