import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useTheme } from '@hatch/ui';
import { getAssignees, getBoard } from '../api/client';
import type { CommandContext, CommandEnv, IssueView } from '../lib/consoleCommands';
import type { GoToLoad } from '../lib/goTo';
import { focusedIssueKey, issueInView } from '../lib/issueInView';
import { scopedPath } from '../lib/scope';
import { isGoToShortcut, isTypingTarget } from '../lib/shortcuts';
import { ConsoleContext } from '../lib/useConsole';
import type { ConsoleControl, ConsoleHost, HeldMoves } from '../lib/useConsole';
import { useCloseSubtree } from '../lib/useCloseSubtree';
import { useMe } from '../lib/useMe';
import { useScope } from '../lib/useScope';
import type { CloseOffer } from '../lib/closeSubtree';
import type { AssigneeDirectory, IssueCard, Status } from '../types';
import { NewIssueDialog } from './NewIssueDialog';
import { OmniBar } from './OmniBar';
import type { MoveOutcome } from './OmniBar';

/** The marker on the board's peek, which the console may open over. */
export const CONSOLE_OVER = 'data-console-over';

/** Whether a dialog other than the peek is open: the console is one to
    everything else, and nothing opens over a form that is being filled in. The
    peek is found by the mark on its body, since Modal draws the panel and takes
    no attributes of its own. */
function dialogOpen(): boolean {
  return [...document.querySelectorAll('[aria-modal="true"]')].some(
    (panel) => !panel.querySelector(`[${CONSOLE_OVER}]`),
  );
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
 * take there rings the card and peeks it, and `>new` opens its own New issue
 * dialog. Anywhere else there is no host, so the console reads the board once,
 * when it opens, and a take opens the issue's page with the scope kept; `>new`
 * mounts the dialog here, over the page, and filing opens the new issue's page.
 * It never polls: it is open for seconds, and the read it made is as good as
 * the person's next keystroke needs.
 *
 * A page that can move issues - the board, and the issue page - registers that
 * too, and the console then offers Status for the issue in view: the page's own,
 * else the peeked card, else the card focused when `/` was pressed. The move is
 * the page's own `send`, so a refusal, a full WIP section and a contribution
 * about to be merged answer as they do from the status bar; and what the move
 * offers to close under it is asked inline, by the console, rather than in the
 * dialog the page would have opened.
 *
 * `/` is left alone where text is being typed, under any dialog but the board's
 * peek, and mid-drag. preventDefault keeps the slash out of the prompt and
 * Firefox's find bar shut.
 */
export function ConsoleProvider({ children }: { children: ReactNode }) {
  const navigate = useNavigate();
  const { pathname } = useLocation();
  const { isAdmin } = useMe();
  const { setChoice } = useTheme();
  const scope = useScope();
  const [open, setOpen] = useState(false);
  const [host, setHostState] = useState<ConsoleHost | null>(null);
  const [read, setRead] = useState<Read>(LOADING);
  const [moves, setMoves] = useState<HeldMoves | null>(null);
  /** The card that had keyboard focus as `/` was pressed, before the prompt took it. */
  const [focusedKey, setFocusedKey] = useState<string | null>(null);
  const [filing, setFiling] = useState(false);
  const [directory, setDirectory] = useState<AssigneeDirectory | null>(null);

  const control = useMemo<ConsoleControl>(() => ({ setHost: setHostState, setMoves }), []);
  const paused = host?.paused ?? false;
  const hosted = host !== null;

  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if (paused || e.defaultPrevented || !isGoToShortcut(e)) return;
      if (isTypingTarget(document.activeElement as HTMLElement | null)) return;
      if (dialogOpen()) return;

      e.preventDefault();
      setRead(LOADING);
      setFocusedKey(focusedIssueKey(document.activeElement));
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

  // Off the board: one read, when the console opens, or when it hands over to
  // the New issue dialog - which needs the same issues as parent candidates, and
  // must not cancel a read still in flight. A console closed before it answers
  // ignores the answer.
  const reading = (open || filing) && !hosted;
  useEffect(() => {
    if (!reading) return;

    let live = true;
    getBoard().then(
      (board) =>
        live &&
        setRead({
          state: 'ready',
          issues: board.issues,
          statuses: board.statuses,
        }),
      () => live && setRead({ state: 'failed', issues: [], statuses: [] }),
    );
    return () => {
      live = false;
    };
  }, [reading]);

  // The dialog's assignee choices, read when it opens and kept for the next time.
  useEffect(() => {
    if (!filing) return;
    getAssignees().then(setDirectory).catch(() => {
      // And without the directory: every control still draws, as on the board.
    });
  }, [filing]);

  const close = useCallback(() => setOpen(false), []);

  /* The question after a move: the same hook the pages use, over the registered
     page's own reload and bookkeeping through wrappers that never change. */
  const held = useRef<HeldMoves | null>(null);
  useEffect(() => {
    held.current = moves;
  }, [moves]);
  const reload = useCallback(() => held.current?.reload() ?? Promise.resolve(), []);
  const closed = useCallback((offer: CloseOffer, changed: string[]) => held.current?.onClosed(offer, changed), []);
  const closing = useCloseSubtree(reload, closed);
  const { close: dropQuestion } = closing;
  // A question left standing when the console goes is not asked again next time.
  useEffect(() => {
    if (!open) dropQuestion();
  }, [open, dropQuestion]);

  const cards = host ? host.cards : read.issues;
  const statuses = host ? host.statuses : read.statuses;
  const viewKey = moves
    ? issueInView({
        pageKey: moves.pageKey,
        peekedKey: moves.peekedKey,
        focusedKey: open ? focusedKey : null,
      })
    : null;
  const view = useMemo<IssueView | null>(() => {
    const card = viewKey === null ? undefined : cards.find((c) => c.key === viewKey);
    if (!card || !moves) return null;
    return {
      card,
      statuses,
      project: moves.projectsByKey.get(card.projectKey) ?? null,
    };
  }, [viewKey, cards, statuses, moves]);

  const onMove = async (key: string, statusId: number): Promise<MoveOutcome> => {
    if (!held.current) return 'moved';
    let offered = false;
    const result = await held.current.send(key, statusId, (offer) => {
      offered = offer !== null;
      closing.ask(offer);
    });
    return result === 'asked' ? 'asked' : offered ? 'offered' : 'moved';
  };

  const onTake = (card: IssueCard) => {
    setOpen(false);
    if (host) host.onTake(card);
    else navigate(scopedPath(`/issues/${card.key}`, scope.keys));
  };

  const onSearch = (path: string) => {
    setOpen(false);
    navigate(path);
  };

  const commandContext = useMemo<CommandContext>(() => ({ isAdmin, pathname, view }), [isAdmin, pathname, view]);

  // What a command that closes the console runs against. The prompt has already
  // given focus back, so a theme change leaves the person where they were; a
  // page change then takes them away from it.
  const env: Pick<CommandEnv, 'go' | 'setTheme' | 'newIssue'> = {
    go: (path) => navigate(scopedPath(path, scope.keys)),
    setTheme: setChoice,
    newIssue: () => (host ? host.onNewIssue() : setFiling(true)),
  };

  return (
    <ConsoleContext.Provider value={control}>
      {children}
      {open && (
        <OmniBar
          cards={cards}
          statuses={statuses}
          visibleKeys={host ? host.visibleKeys : null}
          scopeKeys={scope.keys}
          load={host ? 'ready' : read.state}
          onTake={onTake}
          onSearch={onSearch}
          commandContext={commandContext}
          env={env}
          closing={closing}
          onMove={onMove}
          onClose={close}
        />
      )}
      <NewIssueDialog
        open={filing}
        projects={scope.projects}
        candidates={read.issues}
        defaultProjectKey={scope.keys[0] ?? ''}
        directory={directory}
        onClose={() => setFiling(false)}
        onCreated={(created) => navigate(scopedPath(`/issues/${created.key}`, scope.keys))}
      />
    </ConsoleContext.Provider>
  );
}
