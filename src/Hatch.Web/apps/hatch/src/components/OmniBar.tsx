/* The console: `/` on any page opens a prompt and a list, and taking a row
   goes to that issue, or runs that command.

   The prompt's first character picks what it lists: nothing or `/` is issues,
   `>` is commands, `?` says what the prompt understands. This is the shell:
   the prompt, the list, the keys and the look. What a row is, and what typing
   finds, come from lib/goTo.ts (issues), lib/consoleCommands.ts (commands) and
   lib/consolePrompt.ts (the prefixes), which is where they are tested. What is
   here is state and events.

   Not a <Modal>. Modal is a titled card with a close button and a body 720px
   wide; this is one prompt and a list, and its keyboard is IssuePicker's - the
   input keeps focus while the list is driven, and a click on a row must not
   take it away. It takes on Modal's two duties all the same: it marks itself
   `role="dialog"` and `aria-modal="true"`, which is what the Undo shortcut and
   the confirmation clock both look for, and it gives focus back on dismissal.

   Status is the one command that does not close it. `>status` swaps the list for
   the issue's columns, and a column moves the issue through the page's own
   `send`; if that closes work, what is under the issue is asked as two rows,
   Yes and No, where the status bar would have opened a dialog. A refusal is
   said in the status line and the console stays.

   Mounted only while open, so the prompt starts empty every time - the rule
   IssuePicker states for the same reason. */

import {
  useEffect,
  useLayoutEffect,
  useId,
  useMemo,
  useRef,
  useState,
  type KeyboardEvent,
  type ReactNode,
} from 'react';
import { StatusDot } from './StatusPill';
import { boardColumns } from '../lib/columns';
import { SEARCH_ROW, entryKeys, goToRows, goToStatus, whereOnBoard } from '../lib/goTo';
import type { GoToLoad, Mark } from '../lib/goTo';
import { commandList, commandRows, commandStatus, statusStepRows } from '../lib/consoleCommands';
import type { Command, CommandContext, CommandEnv, CommandStep } from '../lib/consoleCommands';
import { MODE_LABEL, MODE_VERB, PREFIXES, parsePrompt } from '../lib/consolePrompt';
import { closeQuestion, questionRows } from '../lib/consoleQuestion';
import { message } from '../lib/errors';
import { confirmMove } from '../lib/protectedColumns';
import { searchPath } from '../lib/search';
import type { CloseSubtree } from '../lib/useCloseSubtree';
import type { IssueCard, Status } from '../types';

export interface OmniBarProps {
  /** Every issue the board holds, filtered or not - the console narrows them to
      the scope itself. */
  cards: IssueCard[];
  statuses: Status[];
  /** The keys the board's filter lets through, for saying which rows are hidden;
      null off the board, where nothing is. */
  visibleKeys: Set<string> | null;
  /** The tab's scope: `[]` is every project. */
  scopeKeys: string[];
  /** Whether `cards` has landed. */
  load: GoToLoad;
  /** A row was taken. The console does not give focus back: whoever took it is
      about to put it somewhere. */
  onTake: (card: IssueCard) => void;
  /** The trailing row was taken; the console does not give focus back, for the
      reason `onTake` does not. */
  onSearch: (path: string) => void;
  /** Where the person is and who they are, for which commands are offered. */
  commandContext: CommandContext;
  /** What a command that closes the console runs against. */
  env: Pick<CommandEnv, 'go' | 'setTheme'>;
  /** The question after a move that closes work: its state, and what answering
      does. It lives above the console, so the move's own page can hand it an
      offer. */
  closing: CloseSubtree;
  /** Moves the issue in view. Rejects with the server's refusal. */
  onMove: (key: string, statusId: number) => Promise<MoveOutcome>;
  /** The console is done. Focus has already gone back to where it was where the
      person is still on this page - a command that runs, or a dismissal. */
  onClose: () => void;
}

/** How a move came out, from the console's side: nothing to ask, a question to
    ask inline, or a dialog that took over. */
export type MoveOutcome = 'moved' | 'offered' | 'asked';

/** The text with its matched runs lit. */
function Marked({ text, marks }: { text: string; marks: Mark[] }): ReactNode {
  const parts: ReactNode[] = [];
  let at = 0;
  for (const mark of marks) {
    if (mark.start > at) parts.push(text.slice(at, mark.start));
    parts.push(<mark key={mark.start}>{text.slice(mark.start, mark.end)}</mark>);
    at = mark.end;
  }
  if (at < text.length) parts.push(text.slice(at));
  return parts;
}

export function OmniBar({
  cards,
  statuses,
  visibleKeys,
  scopeKeys,
  load,
  onTake,
  onSearch,
  commandContext,
  env,
  closing,
  onMove,
  onClose,
}: OmniBarProps) {
  const [query, setQuery] = useState('');
  /* The second list a command opened, or null. In one, the prompt's text only
     narrows that list: there is no prefix to read. */
  const [step, setStep] = useState<CommandStep | null>(null);
  /* A move is on its way: what is moving and where, for the status line. The
     ref is what the blur and the keys read, since they may fire before a
     render has seen the state. */
  const [moving, setMoving] = useState<{ key: string; column: string } | null>(null);
  const movingNow = useRef(false);
  /* The server's refusal, in its own words, until the person does anything else. */
  const [failure, setFailure] = useState<string | null>(null);
  /* A key, not an index, and null for "the first row". Derived at render below
     rather than kept in step by an effect, so the board's refresh - which hands
     this fresh cards - cannot move it to a different issue. */
  const [highlight, setHighlight] = useState<string | null>(null);

  const input = useRef<HTMLInputElement>(null);
  const activeRow = useRef<HTMLLIElement>(null);
  const opener = useRef<Element | null>(document.activeElement);
  /* Set once a row is taken, so that unmounting a focused panel - which some
     browsers answer with a focusout - does not run the dismissal and pull focus
     back from the card. Also what makes a dismissal happen only once, when a
     scrim press and the blur it causes arrive together. */
  const done = useRef(false);

  const base = useId();
  const listId = `${base}-list`;
  const rowId = (key: string) => `${base}-row-${key}`;
  const searchRowId = `${base}-row-search`;

  const parsed = parsePrompt(query);
  const mode = step ? 'commands' : parsed.mode;
  const asked = step ? query : parsed.query;
  const { isAdmin, pathname, view } = commandContext;
  const offer = closing.offer;

  const { rows, total } = useMemo(
    () => (mode === 'issues' ? goToRows(cards, asked, scopeKeys) : { rows: [], total: 0 }),
    [mode, cards, asked, scopeKeys],
  );
  const commands = useMemo(() => commandList({ isAdmin, pathname, view }), [isAdmin, pathname, view]);
  const stepCommands = useMemo(() => (step && view ? statusStepRows(view) : []), [step, view]);
  const found = useMemo(
    () =>
      step
        ? commandRows(stepCommands, asked, Infinity)
        : mode === 'commands'
          ? commandRows(commands, asked)
          : { rows: [] as Command[], total: 0 },
    [step, stepCommands, mode, commands, asked],
  );
  const answers = useMemo(() => questionRows(), []);
  const columns = useMemo(() => boardColumns(statuses), [statuses]);
  /* One order for every mode, so `move` and `take` stay one code path: help
     has no rows to take, and so no order. */
  const order = offer
    ? answers.map((answer) => answer.id)
    : mode === 'issues'
      ? entryKeys(rows, asked)
      : mode === 'commands'
        ? found.rows.map((command) => command.id)
        : [];
  const active = highlight !== null && order.includes(highlight) ? highlight : (order[0] ?? null);
  const activeIndex = order.indexOf(active ?? '');
  const activeCommand = found.rows.find((command) => command.id === active);

  function dismiss() {
    if (done.current) return;
    done.current = true;
    if (opener.current instanceof HTMLElement) opener.current.focus();
    onClose();
  }

  /* The question is over once its offer goes - answered either way, or closed. */
  const asking = useRef(false);
  const dismissNow = useRef(dismiss);
  useEffect(() => {
    dismissNow.current = dismiss;
  });
  useEffect(() => {
    if (offer) asking.current = true;
    else if (asking.current) {
      asking.current = false;
      dismissNow.current();
    }
  }, [offer]);

  async function startMove(statusId: number) {
    const target = view?.statuses.find((status) => status.id === statusId);
    if (!view || !target || movingNow.current || done.current) return;
    const { key } = view.card;

    // A contribution going where its merge happens is asked about in a dialog
    // over the page, and the move is known to need it already: the console goes
    // first, so the dialog's opener is where the person was.
    if (confirmMove(target, view.card)) {
      done.current = true;
      if (opener.current instanceof HTMLElement) opener.current.focus();
      onClose();
      onMove(key, statusId).catch(() => {});
      return;
    }

    movingNow.current = true;
    setMoving({ key, column: target.name });
    setFailure(null);
    try {
      const outcome = await onMove(key, statusId);
      if (done.current) {
        // Dismissed while it was on its way: an offer made since has nobody to ask.
        if (outcome === 'offered') closing.close();
      } else if (outcome === 'asked') {
        // A dialog took focus, and is about to be where the person is: do not
        // pull it back to where they were.
        done.current = true;
        onClose();
      } else if (outcome === 'moved') dismiss();
    } catch (err) {
      if (!done.current) setFailure(message(err));
    } finally {
      movingNow.current = false;
      setMoving(null);
    }
  }

  function openStep(next: CommandStep) {
    setStep(next);
    setQuery('');
    setHighlight(null);
    setFailure(null);
  }

  function leaveStep() {
    setStep(null);
    setQuery('>');
    setHighlight(null);
    setFailure(null);
  }

  const commandEnv: CommandEnv = {
    ...env,
    move: (statusId) => void startMove(statusId),
    openStep,
  };

  function take(index: number) {
    if (done.current || movingNow.current || index < 0 || index >= order.length) return;
    if (offer) {
      if (closing.busy) return;
      if (order[index] === 'yes') closing.confirm();
      else closing.close();
      return;
    }
    if (mode === 'commands') {
      const command = found.rows[index];
      // Listed so the person can see it is there, and why it cannot be taken.
      if (command.disabled) return;
      if (command.stays) {
        command.run(commandEnv);
        return;
      }
      done.current = true;
      if (opener.current instanceof HTMLElement) opener.current.focus();
      onClose();
      command.run(commandEnv);
      return;
    }
    done.current = true;
    if (order[index] === SEARCH_ROW) onSearch(searchPath(asked)!);
    else onTake(rows[index].card);
  }

  function move(to: number) {
    const key = order[Math.min(Math.max(to, 0), order.length - 1)];
    if (key !== undefined) {
      setHighlight(key);
      setFailure(null);
    }
  }

  function onKeyDown(e: KeyboardEvent<HTMLInputElement>) {
    if (e.key === 'ArrowDown') {
      e.preventDefault(); // so the caret does not jump to the end of the prompt
      move(activeIndex + 1);
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      move(activeIndex - 1);
    } else if (e.key === 'Enter') {
      e.preventDefault();
      take(activeIndex);
    } else if (e.key === 'Escape') {
      e.preventDefault();
      e.stopPropagation();
      // One step back each: out of the question, out of a list, out of the console.
      if (offer) {
        if (!closing.busy) closing.close();
      } else if (step) leaveStep();
      else dismiss();
    }
  }

  // `nearest` so the list does not jump when the row is already on screen.
  useLayoutEffect(() => {
    activeRow.current?.scrollIntoView({ block: 'nearest' });
  }, [active, query]);

  const dialogLabel = offer ? closeQuestion(offer) : step ? 'Set the status' : MODE_LABEL[mode];
  const verb = offer ? 'ask' : step ? 'status' : MODE_VERB[mode];
  const statusLine = moving
    ? `moving ${moving.key} to ${moving.column}…`
    : (failure ??
      (offer
        ? closing.busy
          ? 'closing…'
          : (closing.error ?? 'enter answers')
        : mode === 'commands'
          ? (activeCommand?.disabled ?? commandStatus(found.rows.length, found.total))
          : mode === 'help'
            ? PREFIXES.map((row) => `${row.prefix} ${row.mode === 'help' ? 'this list' : row.mode}`).join(' · ')
            : goToStatus(asked, rows.length, total, load)));
  const rowKey = (key: string) => (key === SEARCH_ROW ? searchRowId : rowId(key));

  return (
    <div
      className="hatch-omni-scrim"
      // A press on the scrim itself and not on the panel. Not a click: the
      // blur it causes would have dismissed already.
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) dismiss();
      }}
    >
      <div
        className="hatch-omni"
        role="dialog"
        aria-modal="true"
        aria-label={dialogLabel}
        /* React's onBlur is focusout, so it bubbles: focus leaving for anywhere
           outside the panel closes it - except while a move is on its way, when
           a dialog it raises takes focus and the move itself says what happens
           to the console. */
        onBlur={(e) => {
          if (movingNow.current) return;
          if (!e.currentTarget.contains(e.relatedTarget)) dismiss();
        }}
        /* A press on anything but the prompt must not take focus off it - a
           blur is a dismissal. The rows say the same for themselves. */
        onMouseDown={(e) => {
          if (e.target !== input.current) e.preventDefault();
        }}
      >
        <div className="hatch-omni-prompt">
          <span className="hatch-omni-verb">{verb}</span>
          <span className="hatch-omni-chevron" aria-hidden="true">
            ❯
          </span>
          {/* type="text", not type="search": Escape inside a search input clears
              it in WebKit and Blink before the keydown reaches us. */}
          <input
            ref={input}
            type="text"
            className="hatch-omni-input"
            autoFocus
            autoComplete="off"
            autoCapitalize="off"
            spellCheck={false}
            value={query}
            aria-label={dialogLabel}
            readOnly={offer !== null}
            role="combobox"
            aria-expanded="true"
            aria-controls={listId}
            aria-autocomplete="list"
            aria-activedescendant={active === null ? undefined : rowKey(active)}
            onChange={(e) => {
              setQuery(e.target.value);
              setHighlight(null);
              setFailure(null);
            }}
            onKeyDown={onKeyDown}
          />
        </div>

        <ul
          className="hatch-omni-list"
          id={listId}
          role={mode === 'help' && !offer ? 'list' : 'listbox'}
          aria-label={offer ? 'Answers' : mode === 'help' ? 'Prefixes' : mode === 'commands' ? 'Commands' : 'Issues'}
        >
          {offer && (
            <li className="hatch-omni-row hatch-omni-row--help hatch-omni-row--question" role="presentation">
              <span className="hatch-omni-marker" aria-hidden="true" />
              <span className="hatch-omni-title">
                <code>{offer.key}</code> is in {offer.column.name}. {closeQuestion(offer)}
              </span>
            </li>
          )}
          {offer &&
            answers.map((answer) => {
              const isActive = answer.id === active;
              return (
                <li
                  key={answer.id}
                  id={rowId(answer.id)}
                  role="option"
                  aria-selected={isActive}
                  ref={isActive ? activeRow : null}
                  className={`hatch-omni-row${isActive ? ' is-active' : ''}`}
                  onMouseDown={(e) => e.preventDefault()}
                  onClick={() => take(order.indexOf(answer.id))}
                  onMouseMove={() => {
                    if (!isActive) setHighlight(answer.id);
                  }}
                >
                  <span className="hatch-omni-marker" aria-hidden="true">
                    {isActive ? '❯' : ''}
                  </span>
                  <span className="hatch-omni-title">{answer.name}</span>
                </li>
              );
            })}
          {mode === 'help' &&
            !offer &&
            PREFIXES.map((row) => (
              <li key={row.prefix} className="hatch-omni-row hatch-omni-row--help">
                <span className="hatch-omni-marker" aria-hidden="true" />
                <span className="hatch-omni-prefix">{row.prefix}</span>
                <span className="hatch-omni-title">{row.line}</span>
              </li>
            ))}
          {(offer ? [] : found.rows).map((command) => {
            const isActive = command.id === active;
            return (
              <li
                key={command.id}
                id={rowId(command.id)}
                role="option"
                aria-selected={isActive}
                ref={isActive ? activeRow : null}
                className={`hatch-omni-row${isActive ? ' is-active' : ''}${command.disabled ? ' is-disabled' : ''}`}
                aria-disabled={command.disabled ? true : undefined}
                title={command.disabled}
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => take(order.indexOf(command.id))}
                onMouseMove={() => {
                  if (!isActive) setHighlight(command.id);
                }}
              >
                <span className="hatch-omni-marker" aria-hidden="true">
                  {isActive ? '❯' : ''}
                </span>
                <span className="hatch-omni-title">{command.name}</span>
                <span className="hatch-omni-type">{command.hint}</span>
              </li>
            );
          })}
          {(offer ? [] : rows).map((row, index) => {
            const { card } = row;
            const status = statuses.find((s) => s.id === card.statusId);
            const isActive = card.key === active;
            const filtered = visibleKeys !== null && whereOnBoard(card, visibleKeys, columns) === 'filtered';

            return (
              <li
                key={card.key}
                id={rowId(card.key)}
                role="option"
                aria-selected={isActive}
                ref={isActive ? activeRow : null}
                className={`hatch-omni-row${isActive ? ' is-active' : ''}`}
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => take(index)}
                /* Move, not enter: a list that scrolls under a resting pointer
                   fires enter for the row that slid beneath it. */
                onMouseMove={() => {
                  if (!isActive) setHighlight(card.key);
                }}
              >
                <span className="hatch-omni-marker" aria-hidden="true">
                  {isActive ? '❯' : ''}
                </span>
                <span className="hatch-omni-key">
                  <Marked text={card.key} marks={row.field === 'key' ? row.marks : []} />
                </span>
                <span className="hatch-omni-type">{card.type}</span>
                <span className="hatch-omni-title">
                  <Marked text={card.title} marks={row.field === 'title' ? row.marks : []} />
                </span>
                {status && (
                  <span className="hatch-omni-column">
                    <StatusDot status={status} />
                    {status.name}
                  </span>
                )}
                {row.outOfScope ? (
                  <span className="hatch-omni-filtered">[out of scope]</span>
                ) : (
                  filtered && <span className="hatch-omni-filtered">[filtered]</span>
                )}
              </li>
            );
          })}
          {!offer && mode === 'issues' && searchPath(asked) !== null && (
            <li
              id={searchRowId}
              role="option"
              aria-selected={active === SEARCH_ROW}
              ref={active === SEARCH_ROW ? activeRow : null}
              className={`hatch-omni-row hatch-omni-row--search${active === SEARCH_ROW ? ' is-active' : ''}`}
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => take(rows.length)}
              onMouseMove={() => {
                if (active !== SEARCH_ROW) setHighlight(SEARCH_ROW);
              }}
            >
              <span className="hatch-omni-marker" aria-hidden="true">
                {active === SEARCH_ROW ? '❯' : ''}
              </span>
              <span className="hatch-omni-title">Search everything for “{asked.trim()}”</span>
            </li>
          )}
        </ul>

        <div className="hatch-omni-status">
          <span aria-live="polite">{statusLine}</span>
          <span className="hatch-omni-keys" aria-hidden="true">
            {(mode !== 'help' || offer) && (
              <>
                <kbd>↑↓</kbd> move <kbd>↵</kbd>{' '}
                {offer ? 'answer' : step ? 'set' : mode === 'commands' ? 'run' : 'open'}{' '}
              </>
            )}
            <kbd>esc</kbd>
          </span>
        </div>
      </div>
    </div>
  );
}
