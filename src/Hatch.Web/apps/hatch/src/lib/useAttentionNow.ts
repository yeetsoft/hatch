import { useEffect, useState } from 'react';

/** How often the waiting phrases are re-read, so `4 minutes` does not sit at
    `4 minutes` for a quarter of an hour while a panel showing them is open.
    The answer itself is polled separately - see `useAttention`. */
export const TICK_MS = 30 * 1000;

/** `now`, ticking every {@link TICK_MS} while `active`, held still otherwise -
    pulled out of NavAttention.tsx so the timer runs only while the dropdown
    `active` gates (`open`) is actually showing its rows, not for as long as
    the bar itself is mounted. */
export function useAttentionNow(active: boolean): Date {
  const [now, setNow] = useState(() => new Date());

  useEffect(() => {
    if (!active) return;
    const timer = setInterval(() => setNow(new Date()), TICK_MS);
    return () => clearInterval(timer);
  }, [active]);

  return now;
}
