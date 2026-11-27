import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useTheme } from '@hatch/ui';
import { getAssignees, getBoard, setPreApproval, setPriority } from '../api/client';
import type { CommandContext, CommandEnv, IssueView } from '../lib/consoleCommands';
import { consoleSource } from '../lib/consoleSource';
import { focusedIssueKey, issueInView } from '../lib/issueInView';
import { HOME } from '../lib/nav';
import { OPEN_QUEUE, scopedPath } from '../lib/scope';
import { isGoToShortcut, isTypingTarget } from '../lib/shortcuts';
import { ConsoleContext } from '../lib/useConsole';
import type { ConsoleControl, ConsoleHost, HeldMoves } from '../lib/useConsole';
import { useCloseSubtree } from '../lib/useCloseSubtree';
import { useMe } from '../lib/useMe';
import { useScope } from '../lib/useScope';
import type { CloseOffer } from '../lib/closeSubtree';
import type { AssigneeDirectory, IssueCard } from '../types';
import { NewIssueDialog } from './NewIssueDialog';
import { OmniBar } from './OmniBar';
import type { MoveOutcome, SetChange } from './OmniBar';

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

/**
 * `/` opens the console from every page.
 *
 * Above <Routes>, so one listener serves them all. The board is the one page
 * that holds the issues already, and it offers them as the console's host: a
 * take there rings the card and peeks it, and `>new` opens its own New issue
 * dialog. Anywhere else there is no host, so the console reads nothing when it
 * opens: its rows come from the projects' numbers and, after a pause in typing,
 * one lookup (see OmniBar). A take opens the issue's page with the scope kept;
 * `>new` mounts the dialog here, over the page, and reads the board for its
 * parent choices - that read belongs to the dialog - and filing opens the new
 * issue's page.
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
  /** The issues the New issue dialog offers as parents, read off the board. */
  const [candidates, setCandidates] = useState<IssueCard[]>([]);
  const [moves, setMoves] = useState<HeldMoves | null>(null);
  /** The card that had keyboard focus as `/` was pressed, before the prompt took it. */
  const [focusedKey, setFocusedKey] = useState<string | null>(null);
  const [filing, setFiling] = useState(false);
  const [directory, setDirectory] = useState<AssigneeDirectory | null>(null);
  const directoryRead = useRef(false);

  const control = useMemo<ConsoleControl>(() => ({ setHost: setHostState, setMoves }), []);
  const paused = host?.paused ?? false;
  const hosted = host !== null;

  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if (paused || e.defaultPrevented || !isGoToShortcut(e)) return;
      if (isTypingTarget(document.activeElement as HTMLElement | null)) return;
      if (dialogOpen()) return;

      e.preventDefault();
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

  // Off the board, the console itself reads nothing. The New issue dialog needs
  // the board's issues as parent candidates, so it reads them when it opens. A
  // dialog closed before the read answers ignores the answer.
  const reading = filing && !hosted;
  useEffect(() => {
    if (!reading) return;

    let live = true;
    getBoard().then(
      (board) => live && setCandidates(board.issues),
      () => live && setCandidates([]),
    );
    return () => {
      live = false;
    };
  }, [reading]);

  // The assignee directory, read when the console opens or hands over to the
  // New issue dialog, and kept for the next time. The dialog draws its choices
  // from it; the console reads who the caller is, to offer Priority and
  // Pre-approval to a person.
  useEffect(() => {
    if (!open && !filing) return;
    if (directoryRead.current) return;
    directoryRead.current = true;
    getAssignees().then(setDirectory).catch(() => {
      // And without the directory: every control still draws, as on the board,
      // and the console offers nothing it cannot be sure the caller may do.
      directoryRead.current = false;
    });
  }, [open, filing]);

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

  const { cards, statuses } = consoleSource(host, moves);
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

  /* The write the screen's own control sends, then the page's own reload, so
     the console and the mouse cannot disagree about what the issue is now. */
  const onSet = async (key: string, change: SetChange): Promise<void> => {
    if (change.kind === 'priority') await setPriority(key, change.level);
    else await setPreApproval(key, change.level);
    await held.current?.reload();
  };

  const onTake = (card: IssueCard) => {
    setOpen(false);
    if (host) host.onTake(card);
    else navigate(scopedPath(`/issues/${card.key}`, scope.keys));
  };

  // A key the page holds no card for: nothing on the board to ring, so its page.
  const onTakeKey = (key: string) => {
    setOpen(false);
    navigate(scopedPath(`/issues/${key}`, scope.keys));
  };

  const onSearch = (path: string) => {
    setOpen(false);
    navigate(path);
  };

  const canSet = directory?.me?.kind === 'person';
  const filters = host?.filters;
  const commandContext = useMemo<CommandContext>(
    () => ({ isAdmin, pathname, view, canSet, filters }),
    [isAdmin, pathname, view, canSet, filters],
  );

  // What a command that closes the console runs against. The prompt has already
  // given focus back, so a theme change leaves the person where they were; a
  // page change then takes them away from it.
  const env: Pick<CommandEnv, 'go' | 'setTheme' | 'changeFilter' | 'newIssue' | 'openQueue'> = {
    go: (path) => navigate(scopedPath(path, scope.keys)),
    setTheme: setChoice,
    // Off the board there is no host, so land on the board with the flag it
    // opens the queue on. On `/` with the board still loading that is the
    // same page: replace, so Back is not handed a duplicate entry.
    openQueue: () =>
      host
        ? host.openQueue()
        : navigate(scopedPath(HOME, scope.keys), { replace: pathname === HOME, state: { [OPEN_QUEUE]: true } }),
    changeFilter: (next) => host?.filters.onChange(next),
    newIssue: () => {
      if (host) return host.onNewIssue();
      // A second filing must not show the first one's parents while this read lands.
      setCandidates([]);
      setFiling(true);
    },
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
          projects={scope.projects}
          allProjects={scope.allProjects}
          scopeIds={scope.ids}
          onTake={onTake}
          onTakeKey={onTakeKey}
          onSearch={onSearch}
          commandContext={commandContext}
          env={env}
          closing={closing}
          onMove={onMove}
          onSet={onSet}
          onClose={close}
        />
      )}
      <NewIssueDialog
        open={filing}
        projects={scope.projects}
        candidates={candidates}
        defaultProjectKey={scope.keys[0] ?? ''}
        directory={directory}
        onClose={() => setFiling(false)}
        onCreated={(created) => navigate(scopedPath(`/issues/${created.key}`, scope.keys))}
      />
    </ConsoleContext.Provider>
  );
}
