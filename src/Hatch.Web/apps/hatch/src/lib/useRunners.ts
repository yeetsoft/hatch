import { useCallback, useEffect, useState } from 'react';
import { getAllRunners, getRunners } from '../api/client';
import type { Runner } from '../types';
import { pollWhileVisible } from './refresh';

/** How often the page re-asks while its tab is on screen. Shorter than the
    nav battery's thirty seconds because this is a control surface somebody is
    watching while they press something: a Pause that took thirty seconds to
    show as paused would read as a button that did nothing. */
export const POLL_MS = 20 * 1000;

export interface RunnersState {
  /** Null until the first answer arrives; an empty list afterwards is a Hatch
      nothing has spoken to lately, which is a different thing and reads as
      one. */
  runners: Runner[] | null;
  /** What went wrong, or null. Unlike the nav battery this does say so: a page
      whose whole purpose is control must not draw a stale row as though it were
      current. */
  error: string | null;
  /** Ask again now - after a press, so the row shows what was just asked for
      without waiting out the interval. */
  reload: () => Promise<void>;
}

/**
 * The runners, kept current.
 *
 * Three things move it, exactly as `useUtilization` is moved and for the same
 * reasons: the first load, an interval, and the tab coming back into view. A
 * hidden tab asks nothing, however long it stays hidden, and asks once at once
 * when it is shown, so a tab brought forward after an hour shows a current
 * page. Focus is not enough on its own, because a tab revealed without being
 * clicked is never focused.
 *
 * A runner changes on its own, all night, which is the whole reason there is a
 * page for one - and the board polls for the same reason. The runners page asks
 * more often (20 s to the board's 30 s) because it is a control surface
 * somebody presses, not a view they glance at.
 *
 * `enabled` is for `QueueControl`, which is mounted on the board all the time:
 * its modal must not poll while closed, so turning this false clears the timer
 * and the listener and turning it true reads at once, if the tab is on
 * screen.
 *
 * `all` reads every runner, revoked and gone ones included, for the runners
 * page; left false it reads the live ones, which is what the board wants.
 */
export function useRunners(enabled = true, all = false): RunnersState {
  const [runners, setRunners] = useState<Runner[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      setRunners(await (all ? getAllRunners() : getRunners()));
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'the runners could not be read');
    }
  }, [all]);

  useEffect(() => {
    if (!enabled) return;

    return pollWhileVisible({ load: () => void load(), everyMs: POLL_MS });
  }, [load, enabled]);

  return { runners, error, reload: load };
}
