import { useCallback, useEffect, useState } from 'react';
import { getAttention } from '../api/client';
import type { Attention } from '../types';

/** How often the bar re-asks.
 *
 *  A minute, rather than the battery's two. This read is three cheap queries
 *  against a table of one household's issues, and it is the only thing in the
 *  app that says a person is being waited on - an answer given four minutes
 *  after the question was asked is a widget doing its job late. */
export const POLL_MS = 60 * 1000;

/**
 * What the loop is waiting on a person for, kept current without a reload.
 *
 * Shaped on `useUtilization`, and for the same reasons. Three things move it:
 *
 * - The first load.
 * - The poll above.
 * - `visibilitychange`. A backgrounded tab's timers are throttled to the point
 *   of stopping, so a tab brought forward after an hour would otherwise draw an
 *   hour-old answer. Focus is not enough on its own: a tab revealed without
 *   being clicked is never focused.
 *
 * Nothing here reports an error. A read that fails leaves the last answer
 * drawn - so the panel behind an already-lit control still lists what it listed
 * a minute ago - and says nothing at all. The bar is not where a fetch
 * failure gets announced, and a control that turned into an error message would
 * be worse than one that is briefly a minute stale.
 *
 * One piece of state, held by the control and passed to the panel: the panel
 * renders what the control already has, so opening and closing it issues no
 * request, and there is no second reading that could disagree with the badge.
 *
 * `reload` is exposed alongside the value for the one caller that cannot wait
 * a minute for the poll to catch up: filing a trunk build's bug (HA-95) has to
 * show the row's new key - and confirm the filing - the moment the press
 * succeeds, for everybody who has the panel open right now.
 */
export function useAttention(): { attention: Attention | null; reload: () => Promise<void> } {
  const [attention, setAttention] = useState<Attention | null>(null);

  const load = useCallback(async () => {
    try {
      setAttention(await getAttention());
    } catch {
      // Deliberately silent - see the note above.
    }
  }, []);

  useEffect(() => {
    void load();

    const timer = setInterval(() => void load(), POLL_MS);
    const onVisible = () => {
      if (document.visibilityState === 'visible') void load();
    };
    document.addEventListener('visibilitychange', onVisible);

    return () => {
      clearInterval(timer);
      document.removeEventListener('visibilitychange', onVisible);
    };
  }, [load]);

  return { attention, reload: load };
}
