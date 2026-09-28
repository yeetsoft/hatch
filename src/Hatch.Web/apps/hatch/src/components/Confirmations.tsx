import { useCallback, useMemo, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { moveIssue } from '../api/client';
import { appHref } from '../lib/basename';
import {
  cascadeClause,
  cascadeOrder,
  canUndo,
  dismiss,
  newestUndoable,
  raise,
  raiseMove,
  settle,
  undoneNote,
  withCascade,
} from '../lib/confirmations';
import type { Confirmation, MovedConfirmation } from '../lib/confirmations';
import { HttpError, message } from '../lib/errors';
import { currentPlatform, undoShortcutLabel } from '../lib/shortcuts';
import { IssueConfirmationsContext } from '../lib/useIssueConfirmations';
import type { IssueConfirmations } from '../lib/useIssueConfirmations';

/**
 * What the corner of the window says about issues filed, and cards moved, in
 * this tab.
 *
 * Filing used to end in silence: the dialog closed, the board reloaded, and the
 * card was somewhere in the leftmost column among the rest - no key to copy,
 * nothing to open, and no record at all of the second one once the third was
 * filed. So every filing raises a chicklet with the key, the title and a link
 * that opens the issue in a new tab, and they stack rather than replace one
 * another. A drop into another column raises one too, saying from where to
 * where, with an Undo on it: a card dragged one lane too far should cost a
 * keystroke, not a hunt for where it came from.
 *
 * They stay until they are closed. Nothing times out, because a timeout is a
 * confirmation that expires while the operator is looking at something else.
 *
 * Local to this app rather than in @hatch/ui: that package's barrel puts every
 * component's CSS in every consuming app's bundle, and there is exactly one
 * consumer of this. It moves the way Modal did - when a second app wants it.
 *
 * The context itself and the hook that reads it are in
 * lib/useIssueConfirmations.ts - see the note there on why they are not here.
 *
 * The provider sits above <Routes> rather than inside a page, because a
 * chicklet held in BoardPage's state would vanish the moment the operator
 * clicked through to the issue they just filed - which is the first thing
 * anybody does with it. It is also why the provider, and not the board, sends
 * the undo: the button works on whatever page the chicklet is showing on. The
 * keyboard shortcut is the board's alone (see BoardPage), so a keystroke made
 * while reading an issue cannot rearrange a board nobody is looking at.
 */
export function ConfirmationsProvider({ children }: { children: ReactNode }) {
  const [stack, setStack] = useState<Confirmation[]>([]);
  /* The stack as of the last change, kept alongside the state. The keyboard
     handler and an undo in flight both need the current stack without
     depending on a render, and two presses inside one frame must each see what
     the first did - which is why every change goes through `change`, not
     through setStack. */
  const held = useRef<Confirmation[]>([]);
  const change = useCallback((f: (prev: Confirmation[]) => Confirmation[]) => {
    held.current = f(held.current);
    setStack(held.current);
  }, []);

  // Monotonic and never reused, so React's key is stable and closing one
  // chicklet can never take a later one filed under the same issue key.
  const nextId = useRef(0);
  const listeners = useRef(new Set<() => void>());

  const confirm = useCallback<IssueConfirmations['confirm']>(
    (issue) => {
      nextId.current += 1;
      const id = nextId.current;
      change((prev) => raise(prev, issue, id));
    },
    [change],
  );

  const moved = useCallback<IssueConfirmations['moved']>(
    (move) => {
      nextId.current += 1;
      const id = nextId.current;
      change((prev) => raiseMove(prev, move, id));
    },
    [change],
  );

  const cascaded = useCallback<IssueConfirmations['cascaded']>(
    (issueKey, entries) => change((prev) => withCascade(prev, issueKey, entries)),
    [change],
  );

  const undo = useCallback<IssueConfirmations['undo']>(
    (id) => {
      const target = held.current.find((c) => c.id === id);
      // Also what makes a second press while one is in flight do nothing.
      if (!target || !canUndo(target)) return;

      change((prev) => settle(prev, id, 'undoing', null));

      void (async () => {
        try {
          // The card first, and alone gating the rest: if it is not where this
          // chicklet left it, nothing under it is touched either.
          await moveIssue(target.issueKey, target.restore);

          // Then what the close offer took with it, one request at a time and
          // top-down - see cascadeOrder. Each is checked against the column the
          // offer put it in, so one that has moved since stays where it is and
          // is named, and the others still go back.
          const stayed: string[] = [];
          for (const entry of cascadeOrder(target.cascade)) {
            try {
              await moveIssue(entry.key, entry.restore);
            } catch (err) {
              stayed.push(err instanceof HttpError && err.status === 409 ? message(err) : `${entry.key} - ${message(err)}`);
            }
          }

          change((prev) => settle(prev, id, 'undone', undoneNote(target, stayed)));
          listeners.current.forEach((listener) => listener());
        } catch (err) {
          // A 409 is the server saying the card is not where this chicklet
          // left it - somebody moved it since. That is final, and the sentence
          // names where it is now. Anything else is a failure that may pass:
          // the button stays so it can be pressed again.
          const refused = err instanceof HttpError && err.status === 409;
          change((prev) => settle(prev, id, refused ? 'refused' : 'failed', message(err)));
        }
      })();
    },
    [change],
  );

  const undoNewest = useCallback(() => {
    const newest = newestUndoable(held.current);
    if (!newest) return false;
    undo(newest.id);
    return true;
  }, [undo]);

  const onUndone = useCallback<IssueConfirmations['onUndone']>((listener) => {
    listeners.current.add(listener);
    return () => void listeners.current.delete(listener);
  }, []);

  // Steady across renders, so a surface holding any of these in a dependency
  // list is not re-running on every keystroke elsewhere.
  const value = useMemo(
    () => ({ confirm, moved, cascaded, undo, undoNewest, onUndone }),
    [confirm, moved, cascaded, undo, undoNewest, onUndone],
  );

  return (
    <IssueConfirmationsContext.Provider value={value}>
      {children}
      <ConfirmationStack
        stack={stack}
        onUndo={undo}
        onDismiss={(id) => change((prev) => dismiss(prev, id))}
        onDismissAll={() => change(() => [])}
      />
    </IssueConfirmationsContext.Provider>
  );
}

/**
 * The bottom-left corner.
 *
 * Drawn whether or not there is anything in it: a live region has to be in the
 * document *before* its content changes for a screen reader to announce the
 * change, so a region that appeared along with the first chicklet would
 * announce nothing. Empty it draws no box and takes no clicks.
 */
function ConfirmationStack({
  stack,
  onUndo,
  onDismiss,
  onDismissAll,
}: {
  stack: Confirmation[];
  onUndo: (id: number) => void;
  onDismiss: (id: number) => void;
  onDismissAll: () => void;
}) {
  return (
    <div className="hatch-confirmations" role="status" aria-live="polite" aria-label="Issues filed and moved">
      {/* Only worth drawing where there is more than one to dismiss: with one
          chicklet its own close control is already the whole of the job. */}
      {stack.length > 1 && (
        <button type="button" className="hatch-confirmations-clear" onClick={onDismissAll}>
          Dismiss all
        </button>
      )}

      <ul className="hatch-confirmations-list">
        {stack.map((c) => (
          <li key={c.id} className="hatch-confirmation">
            {/* An anchor rather than a <Link>: target="_blank" opens a second
                document, which React Router does not route. appHref is what
                keeps that second document on the right prefix - this bundle
                answers at two addresses and only one of them names it. See
                lib/basename.ts. */}
            <a
              className="hatch-confirmation-key"
              href={appHref(`/issues/${c.issueKey}`)}
              target="_blank"
              rel="noreferrer"
            >
              {c.issueKey} ↗
            </a>
            <span className="hatch-confirmation-body">
              <span className="hatch-confirmation-title">{c.title}</span>
              {c.kind === 'moved' && <MoveLine c={c} onUndo={onUndo} />}
            </span>
            {/* Named after what it closes, because "Dismiss" eight times over
                tells a screen reader nothing about which one is which. */}
            <button
              type="button"
              className="hatch-confirmation-close"
              aria-label={`Dismiss ${c.issueKey}`}
              onClick={() => onDismiss(c.id)}
            >
              ×
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}

/**
 * What a move chicklet says under the title: the move, until it has been taken
 * back or has failed to be, and then the sentence that says which.
 *
 * The tooltip names the shortcut for the operator's platform, and
 * `aria-keyshortcuts` names both, because the board answers either modifier.
 * Only a failed undo keeps the button: the other endings are final.
 */
function MoveLine({ c, onUndo }: { c: MovedConfirmation; onUndo: (id: number) => void }) {
  const settled = c.note !== null && c.state !== 'undoing';
  const undoing = c.state === 'undoing';

  return (
    <span className="hatch-confirmation-move">
      <span className={c.state === 'moved' || undoing ? undefined : 'hatch-confirmation-note'}>
        {settled ? c.note : `${c.from.name} → ${c.to.name}${cascadeClause(c)}`}
      </span>
      {(c.state === 'moved' || c.state === 'failed' || undoing) && (
        <button
          type="button"
          className="hatch-confirmation-undo"
          disabled={undoing}
          aria-busy={undoing}
          aria-keyshortcuts="Meta+Z Control+Z"
          title={`Undo (${undoShortcutLabel(currentPlatform())})`}
          onClick={() => onUndo(c.id)}
        >
          {undoing ? 'Undoing…' : 'Undo'}
        </button>
      )}
    </span>
  );
}
