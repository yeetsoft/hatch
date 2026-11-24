import { useCallback, useEffect, useState } from 'react';
import { getUtilization } from '../api/client';
import type { Utilization } from '../types';
import { pollWhileVisible } from './refresh';

/** How often the page re-asks while its tab is on screen. Matches the claim heartbeat's own interval,
    which is the newest reading the server can ever be holding - polling
    faster would just repeat a question the server has already answered.
    A runner's own probe still runs on its own two-minute clock, so a reading
    can still be older than the console even at this pace. */
export const POLL_MS = 30 * 1000;

export interface UtilizationState {
  /** Null both before the first answer and for the 204. The two are the same
      thing to draw - nothing at all - so nothing here tells them apart. */
  reading: Utilization | null;
  /** Asks Hatch to re-read its own row now. */
  refresh: () => Promise<void>;
}

/**
 * The one piece of state behind the battery and its modal.
 *
 * Owned by the nav and passed down, not fetched twice: the modal renders the
 * reading the nav is already holding, so opening and closing it repeatedly
 * issues no request at all, and a refresh from inside it updates both. Two
 * hooks would be two readings that can disagree, which on a number about
 * headroom is the failure worth designing out.
 *
 * Three things move it:
 *
 * - The first load, once the tab is on screen.
 * - A thirty-second poll, while the tab is on screen. The server no longer
 *   holds a reading for five minutes of its own - it holds whatever a runner's
 *   heartbeat last reported - so this is what notices a new one has arrived
 *   without anybody pressing anything.
 * - The tab coming back into view, which asks once at once. A hidden tab asks
 *   nothing, however long it stays hidden, so a tab brought forward after an
 *   hour reads once rather than waiting for the next tick. Focus is not enough
 *   on its own: a tab revealed without being clicked is never focused.
 *
 * Nothing here reports an error. A failure to reach Hatch's own endpoint leaves
 * the last reading in place - and the endpoint's own degraded answer (`stale`)
 * is a reading, not an error. The bar is not where a fetch failure gets
 * announced.
 */
export function useUtilization(): UtilizationState {
  const [reading, setReading] = useState<Utilization | null>(null);

  const load = useCallback(async () => {
    try {
      setReading(await getUtilization());
    } catch {
      // Deliberately silent - see the note above.
    }
  }, []);

  useEffect(() => {
    return pollWhileVisible({ load: () => void load(), everyMs: POLL_MS });
  }, [load]);

  return { reading, refresh: load };
}
