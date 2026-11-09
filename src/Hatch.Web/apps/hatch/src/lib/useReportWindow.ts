import { useMemo } from 'react';
import { useSearchParams } from 'react-router-dom';
import { DEFAULT_DAYS, resolveWindow, sameWindow, withWindow, type ReportWindow } from './reportWindow';

/**
 * A report's window, off the URL, and the write that moves it: the `from` and
 * `to` the page reads between, a `now` fixed for as long as they are, the
 * reader's offset, and `move`, which the window controls call.
 *
 * Calls `useSearchParams` itself and touches nothing else in the URL beyond
 * `from`, `to` and `range`, so a report that keeps filters there keeps them.
 */
export function useReportWindow(defaultDays: number = DEFAULT_DAYS): {
  from: string;
  to: string;
  now: Date;
  offsetMinutes: number;
  move: (next: ReportWindow) => void;
} {
  const [params, setParams] = useSearchParams();

  /* Minutes east of UTC, read once per render rather than held: it is a fact
     about the browser, as on the leaderboard. */
  const offsetMinutes = -new Date().getTimezoneOffset();

  /* Keyed on the URL's own text rather than on `params`, which is a fresh
     object every render - depending on it would resolve a new window each time
     and refetch forever. `now` is read in here too, so it is fixed for as long
     as the window is and the today marker does not drift under a refetch. */
  const search = params.toString();
  const { from, to, now } = useMemo(() => {
    const now = new Date();

    return { ...resolveWindow(new URLSearchParams(search), now, offsetMinutes, defaultDays), now };
  }, [search, offsetMinutes, defaultDays]);

  /* Compares the resolved window, not the URL text, so a press that would land
     where the page already is - Today on the default, with no `from` or `to` in
     the URL at all - writes nothing. Replaces rather than pushes, as the
     leaderboard does: a run of presses is not a run of history entries. */
  function move(next: ReportWindow) {
    if (sameWindow(next, { from, to })) return;

    setParams(withWindow(params, next), { replace: true });
  }

  return { from, to, now, offsetMinutes, move };
}
