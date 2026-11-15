import { createContext, useContext, useEffect, useRef } from 'react';
import type { IssueCard, Status } from '../types';

/**
 * A page that holds the board already, and so can hand the console its issues
 * without a read of its own - and knows what to do with the one that is taken.
 * Only the board is one today. Every other page has no host, and the console
 * reads the board itself and opens the issue's page.
 *
 * The context and its hook rather than the provider, in a file of their own, for
 * the reason lib/useIssueConfirmations.ts gives. The provider and the console it
 * draws are in components/ConsoleProvider.tsx.
 */
export interface ConsoleHost {
  /** Every issue the board holds, filtered or not - the console follows the
      tab's scope on its own. */
  cards: IssueCard[];
  statuses: Status[];
  /** The keys the board's filter lets through, for saying which rows are hidden. */
  visibleKeys: Set<string>;
  /** The board is mid-drag, and `/` must stay out of it. */
  paused: boolean;
  /** A row was taken. The console has closed and does not give focus back: the
      host is about to put it on the card. */
  onTake: (card: IssueCard) => void;
}

export interface ConsoleControl {
  /** Offer the console this page's issues, or take the offer back. Stable, so a
      page that offers does not render again when the console does. */
  setHost: (host: ConsoleHost | null) => void;
}

export const ConsoleContext = createContext<ConsoleControl | null>(null);

/**
 * Be the console's host while this page is on screen. `host` is null while the
 * page has no board to offer. The latest `onTake` is the one called, so a page
 * may pass a new function every render without re-offering its cards.
 */
export function useConsoleHost(host: ConsoleHost | null): void {
  const held = useContext(ConsoleContext);
  if (!held) throw new Error('useConsoleHost outside ConsoleProvider');
  const { setHost } = held;

  const take = useRef<ConsoleHost['onTake'] | null>(null);
  const onTake = host?.onTake ?? null;
  useEffect(() => {
    take.current = onTake;
  }, [onTake]);

  const cards = host?.cards;
  const statuses = host?.statuses;
  const visibleKeys = host?.visibleKeys;
  const paused = host?.paused;

  useEffect(() => {
    if (!cards || !statuses || !visibleKeys || paused === undefined) {
      setHost(null);
      return;
    }
    setHost({ cards, statuses, visibleKeys, paused, onTake: (card) => take.current?.(card) });
  }, [setHost, cards, statuses, visibleKeys, paused]);

  useEffect(() => () => setHost(null), [setHost]);
}
