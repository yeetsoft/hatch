/* The picker's one request: what the server says about keys and titles the
   page's own candidates do not cover.

   Copies lib/useGoTo.ts's shape - the debounce, the abort, the ledger - over
   a simpler rule: there is no `needsLookup` here, since a picker's candidate
   list is never the whole board the way the console's cards are, so every
   settled, non-empty query is asked about. Not a reuse of `useGoTo` itself:
   that hook is tied to the console's `GoToEntry` and its own `needsLookup`. */

import { useEffect, useRef, useState } from 'react';
import { getGoTo } from '../api/client';
import { GO_TO_DEBOUNCE_MS } from './goTo';
import type { PickerLookupFinished } from './pickerLookup';
import { createLedger } from './refresh';

/**
 * @param query      What is typed, as typed.
 * @param enabled    Whether the picker is both open and offered a lookup at
 *                    all. Going false clears the settled query and the last
 *                    answer, so the next opening starts clean.
 * @param projectIds The lookup's scope; none is every project.
 */
export function usePickerLookup(
  query: string,
  enabled: boolean,
  projectIds: number[],
): PickerLookupFinished | null {
  const [settled, setSettled] = useState('');
  const [finished, setFinished] = useState<PickerLookupFinished | null>(null);

  const ledger = useRef(createLedger());
  const flight = useRef<AbortController | null>(null);
  const ids = useRef(projectIds);
  useEffect(() => {
    ids.current = projectIds;
  });

  useEffect(() => {
    if (enabled) return;
    flight.current?.abort();
    flight.current = null;
    ledger.current.invalidate();
    setSettled('');
    setFinished(null);
  }, [enabled]);

  const typed = query.trim();

  useEffect(() => {
    if (!enabled) return;
    const timer = setTimeout(() => setSettled(typed), GO_TO_DEBOUNCE_MS);
    return () => clearTimeout(timer);
  }, [typed, enabled]);

  useEffect(() => {
    if (!enabled || !settled) return;

    flight.current?.abort();
    const abort = new AbortController();
    flight.current = abort;
    const ticket = ledger.current.begin();

    getGoTo(settled, ids.current, abort.signal).then(
      (reply) => {
        if (!ledger.current.isCurrent(ticket)) return;
        flight.current = null;
        setFinished({ query: settled, rows: reply.rows, failed: false });
      },
      () => {
        if (!ledger.current.isCurrent(ticket)) return;
        flight.current = null;
        setFinished({ query: settled, rows: [], failed: true });
      },
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [settled, enabled]);

  useEffect(() => () => flight.current?.abort(), []);

  return finished;
}
