import { createContext, useContext, useEffect, useRef } from 'react';
import type { CloseOffer } from './closeSubtree';
import type { IssueCard, Project, Status } from '../types';

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
  /** `>new` was run. The console has closed; the host opens its own New issue
      dialog, with its own default project and what it does once filed. */
  onNewIssue: () => void;
}

export interface ConsoleControl {
  /** Offer the console this page's issues, or take the offer back. Stable, so a
      page that offers does not render again when the console does. */
  setHost: (host: ConsoleHost | null) => void;
  /** Let the console move issues for this page, or take the offer back. Stable. */
  setMoves: (moves: HeldMoves | null) => void;
}

export const ConsoleContext = createContext<ConsoleControl | null>(null);

/**
 * Be the console's host while this page is on screen. `host` is null while the
 * page has no board to offer. The latest `onTake` and `onNewIssue` are the ones
 * called, so a page may pass new functions every render without re-offering its
 * cards.
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

  const file = useRef<ConsoleHost['onNewIssue'] | null>(null);
  const onNewIssue = host?.onNewIssue ?? null;
  useEffect(() => {
    file.current = onNewIssue;
  }, [onNewIssue]);

  const cards = host?.cards;
  const statuses = host?.statuses;
  const visibleKeys = host?.visibleKeys;
  const paused = host?.paused;

  useEffect(() => {
    if (!cards || !statuses || !visibleKeys || paused === undefined) {
      setHost(null);
      return;
    }
    setHost({ cards, statuses, visibleKeys, paused, onTake: (card) => take.current?.(card), onNewIssue: () => file.current?.() });
  }, [setHost, cards, statuses, visibleKeys, paused]);

  useEffect(() => () => setHost(null), [setHost]);
}

/** How a move came out. `asked` means a dialog took over - a full WIP section,
    or a contribution about to be merged - and will finish the move itself. */
export type MoveResult = 'moved' | 'asked';

/**
 * A page that can move an issue to a column, and so lets the console do it.
 *
 * Offered by the board, and by the issue page, which is not a host: the console
 * still reads the board for it. Every function is the page's own - the one its
 * status bar or peek calls - so the console and the mouse go through the same
 * refusals and the same dialogs, and cannot come to disagree.
 */
export interface ConsoleMoves {
  /** The issue the page is about, or null where it is about the board. */
  pageKey: string | null;
  /** The card the board's peek is open on. */
  peekedKey: string | null;
  projectsByKey: Map<string, Project>;
  /** Moves `key` to the column. `onOffer` is told what the move offers to close
      under it - null for nothing - in place of the page's own dialog. A refusal
      rejects with the server's error. */
  send: (key: string, statusId: number, onOffer: (offer: CloseOffer | null) => void) => Promise<MoveResult>;
  /** Catches the page up after a cascade. */
  reload: () => Promise<void>;
  /** Told which keys a cascade moved. */
  onClosed?: (offer: CloseOffer, changed: string[]) => void;
}

/** What the console holds of a page's moves: its facts, and the page's
    functions through stable wrappers that call the latest of them. */
export type HeldMoves = Omit<ConsoleMoves, 'onClosed'> & {
  onClosed: NonNullable<ConsoleMoves['onClosed']>;
};

/**
 * Let the console move issues while this page is on screen. `moves` is null
 * while the page cannot. The latest functions are the ones called, so a page
 * may pass new ones every render without registering again.
 */
export function useConsoleMoves(moves: ConsoleMoves | null): void {
  const held = useContext(ConsoleContext);
  if (!held) throw new Error('useConsoleMoves outside ConsoleProvider');
  const { setMoves } = held;

  const latest = useRef<ConsoleMoves | null>(null);
  useEffect(() => {
    latest.current = moves;
  });

  const pageKey = moves?.pageKey;
  const peekedKey = moves?.peekedKey;
  const projectsByKey = moves?.projectsByKey;
  const offered = moves !== null;

  useEffect(() => {
    if (!offered || pageKey === undefined || peekedKey === undefined || !projectsByKey) {
      setMoves(null);
      return;
    }
    setMoves({
      pageKey,
      peekedKey,
      projectsByKey,
      send: (key, statusId, onOffer) => latest.current?.send(key, statusId, onOffer) ?? Promise.resolve('moved'),
      reload: () => latest.current?.reload() ?? Promise.resolve(),
      onClosed: (offer, changed) => latest.current?.onClosed?.(offer, changed),
    });
  }, [setMoves, offered, pageKey, peekedKey, projectsByKey]);

  useEffect(() => () => setMoves(null), [setMoves]);
}
