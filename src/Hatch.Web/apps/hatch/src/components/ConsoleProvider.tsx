import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { getBoard } from '../api/client';
import type { GoToLoad } from '../lib/goTo';
import { scopedPath } from '../lib/scope';
import { isGoToShortcut, isTypingTarget } from '../lib/shortcuts';
import { ConsoleContext } from '../lib/useConsole';
import type { ConsoleControl, ConsoleHost } from '../lib/useConsole';
import { useScope } from '../lib/useScope';
import type { IssueCard, Status } from '../types';
import { OmniBar } from './OmniBar';

/** The marker on the board's peek, which the console may open over. */
export const CONSOLE_OVER = 'data-console-over';

/** Whether a dialog other than the peek is open: the console is one to
    everything else, and nothing opens over a form that is being filled in. The
    peek is found by the mark on its body, since Modal draws the panel and takes
    no attributes of its own. */
function dialogOpen(): boolean {
  return [...document.querySelectorAll('[aria-modal="true"]')].some((panel) => !panel.querySelector(`[${CONSOLE_OVER}]`));
}

/** The issues and columns read for a page that is not a host. */
interface Read {
  state: GoToLoad;
  issues: IssueCard[];
  statuses: Status[];
}

const LOADING: Read = { state: 'loading', issues: [], statuses: [] };

/**
 * `/` opens the console from every page.
 *
 * Above <Routes>, so one listener serves them all. The board is the one page
 * that holds the issues already, and it offers them as the console's host: a
 * take there rings the card and peeks it. Anywhere else there is no host, so the
 * console reads the board once, when it opens, and a take opens the issue's page
 * with the scope kept. It never polls: it is open for seconds, and the read it
 * made is as good as the person's next keystroke needs.
 *
 * `/` is left alone where text is being typed, under any dialog but the board's
 * peek, and mid-drag. preventDefault keeps the slash out of the prompt and
 * Firefox's find bar shut.
 */
export function ConsoleProvider({ children }: { children: ReactNode }) {
  const navigate = useNavigate();
  const scope = useScope();
  const [open, setOpen] = useState(false);
  const [host, setHostState] = useState<ConsoleHost | null>(null);
  const [read, setRead] = useState<Read>(LOADING);

  const control = useMemo<ConsoleControl>(() => ({ setHost: setHostState }), []);
  const paused = host?.paused ?? false;
  const hosted = host !== null;

  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if (paused || e.defaultPrevented || !isGoToShortcut(e)) return;
      if (isTypingTarget(document.activeElement as HTMLElement | null)) return;
      if (dialogOpen()) return;

      e.preventDefault();
      setRead(LOADING);
      setOpen(true);
    };

    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [paused]);

  // The page that offered its issues went away while the console was open - the
  // board was left by the browser's Back, say: close rather than list a board
  // that is no longer there.
  const wasHosted = useRef(false);
  useEffect(() => {
    if (wasHosted.current && !hosted) setOpen(false);
    wasHosted.current = hosted;
  }, [hosted]);

  // Off the board: one read, when the console opens. A console closed before it
  // answers ignores the answer.
  useEffect(() => {
    if (!open || hosted) return;

    let live = true;
    getBoard().then(
      (board) => live && setRead({ state: 'ready', issues: board.issues, statuses: board.statuses }),
      () => live && setRead({ state: 'failed', issues: [], statuses: [] }),
    );
    return () => {
      live = false;
    };
  }, [open, hosted]);

  const close = useCallback(() => setOpen(false), []);

  const onTake = (card: IssueCard) => {
    setOpen(false);
    if (host) host.onTake(card);
    else navigate(scopedPath(`/issues/${card.key}`, scope.keys));
  };

  const onSearch = (path: string) => {
    setOpen(false);
    navigate(path);
  };

  return (
    <ConsoleContext.Provider value={control}>
      {children}
      {open && (
        <OmniBar
          cards={host ? host.cards : read.issues}
          statuses={host ? host.statuses : read.statuses}
          visibleKeys={host ? host.visibleKeys : null}
          scopeKeys={scope.keys}
          load={host ? 'ready' : read.state}
          onTake={onTake}
          onSearch={onSearch}
          onClose={close}
        />
      )}
    </ConsoleContext.Provider>
  );
}
