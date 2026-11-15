import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useTheme } from '@hatch/ui';
import { getAssignees, getBoard } from '../api/client';
import type { Command, CommandContext } from '../lib/consoleCommands';
import type { GoToLoad } from '../lib/goTo';
import { scopedPath } from '../lib/scope';
import { isGoToShortcut, isTypingTarget } from '../lib/shortcuts';
import { ConsoleContext } from '../lib/useConsole';
import type { ConsoleControl, ConsoleHost } from '../lib/useConsole';
import { useMe } from '../lib/useMe';
import { useScope } from '../lib/useScope';
import type { AssigneeDirectory, IssueCard, Status } from '../types';
import { NewIssueDialog } from './NewIssueDialog';
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
 * take there rings the card and peeks it, and `>new` opens its own New issue
 * dialog. Anywhere else there is no host, so the console reads the board once,
 * when it opens, and a take opens the issue's page with the scope kept; `>new`
 * mounts the dialog here, over the page, and filing opens the new issue's page.
 * It never polls: it is open for seconds, and the read it made is as good as
 * the person's next keystroke needs.
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
  const [filing, setFiling] = useState(false);
  const [directory, setDirectory] = useState<AssigneeDirectory | null>(null);

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

  // Off the board: one read, when the console opens, or when it hands over to
  // the New issue dialog - which needs the same issues as parent candidates, and
  // must not cancel a read still in flight. A console closed before it answers
  // ignores the answer.
  const reading = (open || filing) && !hosted;
  useEffect(() => {
    if (!reading) return;

    let live = true;
    getBoard().then(
      (board) => live && setRead({ state: 'ready', issues: board.issues, statuses: board.statuses }),
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

  const onTake = (card: IssueCard) => {
    setOpen(false);
    if (host) host.onTake(card);
    else navigate(scopedPath(`/issues/${card.key}`, scope.keys));
  };

  const onSearch = (path: string) => {
    setOpen(false);
    navigate(path);
  };

  const commandContext = useMemo<CommandContext>(() => ({ isAdmin, pathname }), [isAdmin, pathname]);

  // The prompt has already given focus back, so a theme change leaves the
  // person where they were; a page change then takes them away from it.
  const onRun = (command: Command) => {
    setOpen(false);
    command.run({
      go: (path) => navigate(scopedPath(path, scope.keys)),
      setTheme: setChoice,
      newIssue: () => (host ? host.onNewIssue() : setFiling(true)),
    });
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
          commandContext={commandContext}
          onRun={onRun}
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
