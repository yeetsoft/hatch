import { useCallback, useEffect, useRef, useState } from 'react';
import type { Dispatch, SetStateAction } from 'react';
import { message } from './errors';
import { createLedger, mayApply, mayRefresh } from './refresh';

export interface LoadedOptions {
  /** Also reload on this interval while the page is on screen, and at once when
      it comes back on screen. Unset, the data reloads on action and focus only. */
  everyMs?: number;
  /** Hold the data still: no timed or focus refresh starts, and one already on
      its way is dropped when it lands. Read through a ref, so toggling it neither
      resets the interval nor re-subscribes. */
  paused?: boolean;
}

/**
 * Data from the API, reloaded after every action and whenever the tab is
 * focused again - and, for a caller that asks with `everyMs`, on an interval
 * while the page is on screen and at once when it returns to the screen.
 *
 * Most pages take the default. A page nobody edits but the operator has no need
 * of a timer: the moment they look back at the tab is the moment a stale page
 * would be noticed, so focus is where the refetch goes. The board is the
 * exception, because the loop moves cards all night with no one watching, and
 * `visibilitychange` sits beside focus for the reason `useAttention` gives: a
 * tab revealed without being clicked is never focused.
 *
 * Answers are kept by the latest-wins rule in `lib/refresh`. Two reloads that
 * overlap can resolve in either order, and a reload begun before the caller
 * wrote the data itself with `setData` (an optimistic move) would otherwise draw
 * the old state back over it. A stale answer is dropped whole - neither the data
 * nor the error is touched, so an old failure cannot paint over a newer board.
 * Leaving the effect drops what is in flight too, so an answer for a `load`
 * that has been swapped out cannot land after the swap.
 *
 * Reloads are of two kinds. An explicit one - the `reload` this returns, and the
 * first load - always applies if it is the latest, paused or not, because the
 * caller asking is the one holding the pause. A background one (interval,
 * `visibilitychange`, focus) may start only while visible and not paused, and
 * applies only if it is not paused when it lands. A background answer dropped
 * for that is not queued: the next tick, at most `everyMs` away, makes it good.
 *
 * `load` has to be stable across renders (a module-level function, or one
 * wrapped in useCallback) or this reloads on every render.
 */
export function useLoaded<T>(load: () => Promise<T>, options: LoadedOptions = {}) {
  const { everyMs, paused = false } = options;
  const [data, setRawData] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [ledger] = useState(createLedger);

  const pausedRef = useRef(paused);
  useEffect(() => {
    pausedRef.current = paused;
  });

  const run = useCallback(
    async (background: boolean) => {
      const ticket = ledger.begin();
      const applies = () =>
        mayApply({ background, current: ledger.isCurrent(ticket), paused: pausedRef.current });
      try {
        const loaded = await load();
        if (!applies()) return;
        setRawData(loaded);
        setError(null);
      } catch (err) {
        if (!applies()) return;
        setError(message(err));
      }
    },
    [load, ledger],
  );

  const reload = useCallback(() => run(false), [run]);

  // Writing the data directly makes any load already on its way stale.
  const setData: Dispatch<SetStateAction<T | null>> = useCallback(
    (next) => {
      ledger.invalidate();
      setRawData(next);
    },
    [ledger],
  );

  useEffect(() => {
    void run(false);

    const refreshIf = (visible: boolean) => {
      if (mayRefresh({ visible, paused: pausedRef.current })) void run(true);
    };
    const onFocus = () => refreshIf(true);
    window.addEventListener('focus', onFocus);

    let timer: ReturnType<typeof setInterval> | undefined;
    const onVisibility = () => refreshIf(document.visibilityState === 'visible');
    if (everyMs !== undefined) {
      timer = setInterval(() => refreshIf(document.visibilityState === 'visible'), everyMs);
      document.addEventListener('visibilitychange', onVisibility);
    }

    return () => {
      window.removeEventListener('focus', onFocus);
      if (everyMs !== undefined) {
        clearInterval(timer);
        document.removeEventListener('visibilitychange', onVisibility);
      }
      ledger.invalidate();
    };
  }, [run, everyMs, ledger]);

  return { data, setData, error, setError, reload };
}
