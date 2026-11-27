/* The console's one request: what the server says about the keys the page has
   listed but holds no card for.

   Typing is not a request. The prompt settles for `GO_TO_DEBOUNCE_MS` first, and
   `needsLookup` (lib/goToKeys.ts) then decides whether anything is left to ask.
   A request still out when the query moves on is aborted and its answer ignored,
   by the same ledger the pages' reads use (lib/refresh.ts), so an answer to
   yesterday's query cannot be drawn under today's.

   The hook is mounted with the console, which is mounted only while open, so the
   last answer it keeps is the opening's: a narrowing query after a complete
   answer sends nothing. */

import { useEffect, useRef, useState } from 'react';
import { getGoTo } from '../api/client';
import { GO_TO_DEBOUNCE_MS } from './goTo';
import { needsLookup, scopeToken } from './goToKeys';
import type { GoToAnswered, GoToEntry } from './goToKeys';
import { createLedger } from './refresh';

export interface UseGoTo {
  /** The last answer received this opening, or null. */
  answer: GoToAnswered | null;
  /** A request is out. */
  pending: boolean;
}

/**
 * @param query    What the prompt asks about, as typed.
 * @param enabled  Whether the prompt is listing issues at all.
 * @param scopeIds The tab's scope, as project ids; none is every project.
 * @param list     What is listed for a query right now. Read through a ref, at
 *                 the moment typing settles: the board refreshing its cards
 *                 under an open console sends nothing.
 */
export function useGoTo(
  query: string,
  enabled: boolean,
  scopeIds: number[],
  list: (query: string) => GoToEntry[],
): UseGoTo {
  const [settled, setSettled] = useState('');
  const [answer, setAnswer] = useState<GoToAnswered | null>(null);
  const [pending, setPending] = useState(false);

  const listNow = useRef(list);
  const ledger = useRef(createLedger());
  const flight = useRef<{ abort: AbortController; query: string } | null>(null);
  const last = useRef<GoToAnswered | null>(null);
  useEffect(() => {
    listNow.current = list;
  });

  const scope = scopeToken(scopeIds);
  const ids = useRef(scopeIds);
  useEffect(() => {
    ids.current = scopeIds;
  });

  const typed = query.trim();

  function cancel() {
    flight.current?.abort.abort();
    flight.current = null;
    ledger.current.invalidate();
    setPending(false);
  }

  // Typing goes on: a request for what was typed a moment ago is no longer wanted.
  // The settled query is cleared with it, so that typing back to the very text
  // that was cancelled settles as a change and asks again.
  useEffect(() => {
    if (flight.current && flight.current.query !== typed) {
      cancel();
      setSettled('');
    }
  }, [typed]);

  // The prompt pauses.
  useEffect(() => {
    if (!enabled) return;

    const timer = setTimeout(() => setSettled(typed), GO_TO_DEBOUNCE_MS);
    return () => clearTimeout(timer);
  }, [typed, enabled]);

  useEffect(() => {
    if (!enabled || !settled) {
      if (flight.current) cancel();
      return;
    }

    if (!needsLookup(listNow.current(settled), settled, scope, last.current)) {
      if (flight.current) cancel();
      return;
    }

    flight.current?.abort.abort();
    const abort = new AbortController();
    const ticket = ledger.current.begin();
    flight.current = { abort, query: settled };
    setPending(true);

    getGoTo(settled, ids.current, abort.signal).then(
      (reply) => {
        if (!ledger.current.isCurrent(ticket)) return;

        const next: GoToAnswered = { query: settled, scope, more: reply.more, rows: reply.rows };
        last.current = next;
        flight.current = null;
        setAnswer(next);
        setPending(false);
      },
      () => {
        // The rows stay as placeholders, and nothing is retried until the query
        // or the scope changes: a failing server is not hammered once a pause.
        if (!ledger.current.isCurrent(ticket)) return;
        flight.current = null;
        setPending(false);
      },
    );
    // `cancel` and the refs are stable in effect; only a settled query or scope asks.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [settled, enabled, scope]);

  // The console closes while a request is out.
  useEffect(
    () => () => {
      flight.current?.abort.abort();
      ledger.current.invalidate();
    },
    [],
  );

  return { answer, pending };
}
