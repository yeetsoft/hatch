import { createContext, useContext, useEffect, useRef } from 'react';
import type { FilterHandle } from './consoleCommands';
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
  /** Open the work queue modal the board owns. */
  openQueue: () => void;
  /** `>new` was run. The console has closed; the host opens its own New issue
      dialog, with its own default project and what it does once filed. */
  onNewIssue: () => void;
  /** The board's filter, for the commands that set it. */
  filters: FilterHandle;
}

export interface ConsoleControl {
  /** Offer the console this page's issues, or take the offer back. Stable, so a
      page that offers does not render again when the console does. */
  setHost: (host: ConsoleHost | null) => void;
}

export const ConsoleContext = createContext<ConsoleControl | null>(null);

/**
 * Be the console's host while this page is on screen. `host` is null while the
 * page has no board to offer. The latest `onTake`, `onNewIssue`, `openQueue` and
 * `filters.onChange` are the ones called, so a page may pass new functions every
 * render without re-offering its cards.
 */
export function useConsoleHost(host: ConsoleHost | null): void {
  const held = useContext(ConsoleContext);
  if (!held) throw new Error('useConsoleHost outside ConsoleProvider');
  const { setHost } = held;

  const take = useRef<ConsoleHost['onTake'] | null>(null);
  const onTake = host?.onTake ?? null;
  const queue = useRef<ConsoleHost['openQueue'] | null>(null);
  const openQueue = host?.openQueue ?? null;
  useEffect(() => {
    take.current = onTake;
    queue.current = openQueue;
  }, [onTake, openQueue]);

  const file = useRef<ConsoleHost['onNewIssue'] | null>(null);
  const onNewIssue = host?.onNewIssue ?? null;
  useEffect(() => {
    file.current = onNewIssue;
  }, [onNewIssue]);

  const change = useRef<FilterHandle['onChange'] | null>(null);
  const onChange = host?.filters.onChange ?? null;
  useEffect(() => {
    change.current = onChange;
  }, [onChange]);

  const cards = host?.cards;
  const statuses = host?.statuses;
  const visibleKeys = host?.visibleKeys;
  const paused = host?.paused;
  const filter = host?.filters.filter;
  const projects = host?.filters.projects;

  useEffect(() => {
    if (!cards || !statuses || !visibleKeys || paused === undefined || !filter || !projects) {
      setHost(null);
      return;
    }
    setHost({
      cards,
      statuses,
      visibleKeys,
      paused,
      onTake: (card) => take.current?.(card),
      onNewIssue: () => file.current?.(),
      openQueue: () => queue.current?.(),
      filters: { filter, projects, onChange: (next) => change.current?.(next) },
    });
  }, [setHost, cards, statuses, visibleKeys, paused, filter, projects]);

  useEffect(() => () => setHost(null), [setHost]);
}
