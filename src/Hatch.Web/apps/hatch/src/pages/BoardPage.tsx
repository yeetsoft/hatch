import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  DndContext,
  DragOverlay,
  KeyboardSensor,
  MouseSensor,
  TouchSensor,
  useDroppable,
  useSensor,
  useSensors,
} from '@dnd-kit/core';
import type { DragEndEvent, DragOverEvent, DragStartEvent } from '@dnd-kit/core';
import { SortableContext, sortableKeyboardCoordinates, verticalListSortingStrategy } from '@dnd-kit/sortable';
import { Button, EmptyState } from '@hatch/ui';
import { getAssignees, getBoard, moveIssue } from '../api/client';
import { BoardCard, CardPreview } from '../components/BoardCard';
import { BoardFilters } from '../components/BoardFilters';
import { CloseSubtreeDialog } from '../components/CloseSubtreeDialog';
import { IssuePeek } from '../components/IssuePeek';
import { NewIssueDialog } from '../components/NewIssueDialog';
import { OmniBar } from '../components/OmniBar';
import { QueueControl } from '../components/QueueControl';
import { StatusDot } from '../components/StatusPill';
import { WipOverrideDialog } from '../components/WipOverrideDialog';
import { statusVars } from '../lib/color';
import { closeOffer } from '../lib/closeSubtree';
import type { CloseOffer } from '../lib/closeSubtree';
import { cascadeEntries, dropConfirmation } from '../lib/confirmations';
import type { CascadeEntry } from '../lib/confirmations';
import { boardColumns } from '../lib/columns';
import { message } from '../lib/errors';
import { DEFAULT_FILTER, assigneeFacets, filterCards, isFiltering, revealType } from '../lib/filter';
import type { CardFilter } from '../lib/filter';
import { aimAt } from '../lib/aim';
import { whereOnBoard } from '../lib/goTo';
import { readBoardView, resolveBoardLayout, startsCollapsed, writeBoardView } from '../lib/phoneBoard';
import type { BoardView } from '../lib/phoneBoard';
import { columnDroppableId, place, sendTo, targetStatusId } from '../lib/place';
import type { Placement } from '../lib/place';
import { askingCount } from '../lib/questions';
import { isWaiting } from '../lib/schedule';
import { isGoToShortcut, isTypingTarget, isUndoShortcut } from '../lib/shortcuts';
import { useCloseSubtree } from '../lib/useCloseSubtree';
import { useIssueConfirmations } from '../lib/useIssueConfirmations';
import { useLoaded } from '../lib/useLoaded';
import { hasMultipleProjects, useProjects } from '../lib/useProjects';
import { usePhone } from '../lib/viewport';
import { useWipOverride } from '../lib/useWipOverride';
import { overridden, wipRefusal } from '../lib/wipOverride';
import { meterText, preview, runs, tightest } from '../lib/wip';
import type { Tightness } from '../lib/wip';
import { WipBands } from '../components/WipBands';
import type { AssigneeDirectory, Board, IssueCard, Project, Status, Wip } from '../types';

/** The URL's whole vocabulary here: which project, by key - the same param
    PlanPage's picker uses. */
const PROJECT = 'project';

/** How often the board re-asks while it is on screen.
 *
 *  Half a runner's heartbeat. A runner beats at a fifth of its lease - 60 s at
 *  the default 300 s - and that is how often a card's claim and its chatter can
 *  change, so 30 s keeps the board within one heartbeat of the runner. Between
 *  the runners page's 20 s (a control surface under a finger) and the attention
 *  bar's minute: most moves on this board are made by the loop, with nobody
 *  watching, and a read is a few cheap queries. */
export const POLL_MS = 30 * 1000;

/** How long the ring stays on a card the console found once its peek has
 *  closed: long enough to see where the eye was sent, short enough not to look
 *  like the card is selected. */
export const FOUND_LINGER_MS = 2000;

/** How long a touch has to hold still before it lifts a card. A deliberate
 *  press-and-hold, long enough that the first frames of a real scroll exceed
 *  TOUCH_LIFT_TOLERANCE_PX and cancel it. */
const TOUCH_LIFT_DELAY_MS = 300;

/** How far a touch may wander while holding still and still count as the
 *  hold. Loose enough for a finger's natural wobble, tight enough that a
 *  scroll gesture clears it almost immediately. */
const TOUCH_LIFT_TOLERANCE_PX = 8;

export function BoardPage() {
  const isPhone = usePhone();

  // The chosen layout at phone width, seeded from storage and written on
  // every press of the toggle - off the phone this is always 'full' (AC7),
  // whatever is stored. See lib/phoneBoard.ts.
  const [view, setView] = useState<BoardView>(readBoardView);
  const layout = resolveBoardLayout(isPhone, view);
  const stacked = layout === 'stacked';

  // The card under the cursor and the column it is over, kept only for the
  // duration of a drag: one paints the overlay, the other lights up the column
  // the drop would land in. Above the loader, which pauses its refresh for both.
  const [dragging, setDragging] = useState<IssueCard | null>(null);
  const [over, setOver] = useState<number | null>(null);
  /* How many moves are on their way to the server. A drop paints the card in its
     new column before the request, and a refresh landing in that window would
     draw it back. A count and not a flag, so a second drop made while the first
     is still out cannot lift the guard early. */
  const [moving, setMoving] = useState(0);

  const {
    data: board,
    setData: setBoard,
    error,
    setError,
    reload,
  } = useLoaded<Board>(getBoard, { everyMs: POLL_MS, paused: dragging !== null || moving > 0 });
  const { projects, byKey: projectsByKey } = useProjects();
  const showProjectMark = hasMultipleProjects(projects);
  /* Who is signed in, read once for the whole board rather than once per card
     opened. The summary needs it to know whether to offer the expedite press -
     the write is a person's - and a dialog that fetched its own copy would ask
     again every time somebody clicked a card. Null where it could not be read,
     which leaves the press unoffered and the state still drawn. */
  const [directory, setDirectory] = useState<AssigneeDirectory | null>(null);
  const [filing, setFiling] = useState(false);
  const [params, setParams] = useSearchParams();
  /* Only the project half of the filter lives in the URL - see setFilter
     below - so it is read once here, on the way into local state, rather than
     off `params` on every render. */
  const [filter, setFilter] = useState<CardFilter>(() => ({ ...DEFAULT_FILTER, project: params.get(PROJECT) ?? '' }));

  /* Wraps the plain setter so a change to the project also writes (or drops)
     `?project=` - replaced rather than pushed, exactly as PlanPage's picker
     does, since flipping a filter is not a place worth six presses of Back. */
  const changeFilter = useCallback(
    (next: CardFilter) => {
      setFilter((prev) => {
        if (next.project !== prev.project) {
          const nextParams = new URLSearchParams(params);
          if (next.project) nextParams.set(PROJECT, next.project);
          else nextParams.delete(PROJECT);
          setParams(nextParams, { replace: true });
        }
        return next;
      });
    },
    [params, setParams],
  );

  /** The card a click opened a summary for. Null when the dialog is closed. */
  const [peeking, setPeeking] = useState<IssueCard | null>(null);

  /* The console, and the card it found. `found` is a key, drawn as a ring; it
     outlives the peek by FOUND_LINGER_MS. `pending` is the card a take has
     chosen and not yet peeked, for the one render in which the board has
     opened its fold and can be asked where the card is. */
  const [going, setGoing] = useState(false);
  const [found, setFound] = useState<string | null>(null);
  const [pending, setPending] = useState<IssueCard | null>(null);
  const linger = useRef<number | undefined>(undefined);
  const stopLinger = useCallback(() => window.clearTimeout(linger.current), []);
  useEffect(() => stopLinger, [stopLinger]);

  // A click on a card is somewhere else to look: the ring is done.
  const peek = useCallback(
    (card: IssueCard) => {
      stopLinger();
      setFound(null);
      setPeeking(card);
    },
    [stopLinger],
  );

  const closePeek = useCallback(() => {
    setPeeking(null);
    stopLinger();
    linger.current = window.setTimeout(() => setFound(null), FOUND_LINGER_MS);
  }, [stopLinger]);

  // The corner's chicklets: a drop raises one, and its Undo moves the card
  // back from wherever the operator is by then.
  const { moved, cascaded, undoNewest, onUndone } = useIssueConfirmations();

  /* Where each descendant of a card offered a close stood before the drop,
     held by the card's key until the operator answers. By then the board has
     been repainted and reloaded, and the answer to "back to where?" is gone
     from it. */
  const beforeClose = useRef(new Map<string, CascadeEntry[]>());
  const closed = useCallback(
    (offer: CloseOffer, changed: string[]) => {
      const entries = (beforeClose.current.get(offer.key) ?? []).filter((e) => changed.includes(e.key));
      cascaded(offer.key, entries);
    },
    [cascaded],
  );

  // The offer to close everything under a card dropped into a terminal column.
  const closing = useCloseSubtree(reload, closed);
  const { ask } = closing;

  // The dialog a drop into a full WIP section raises. `reload` is its
  // `onLeave`: Leave it, or dismissing it, catches the board up with the card
  // commit's own catch has already put back.
  const wip = useWipOverride(reload);

  useEffect(() => {
    getAssignees().then(setDirectory).catch(() => {
      // And without the directory: every card still draws, and the summary
      // says whether an issue is expedited without offering to change it.
    });
  }, []);

  useEffect(() => {
    // A key in the URL that names no project reads as all projects, and
    // the param is dropped - the picker has nothing to show it as chosen.
    setFilter((prev) => {
      if (prev.project === '' || projects.some((p) => p.key === prev.project)) return prev;
      const nextParams = new URLSearchParams(params);
      nextParams.delete(PROJECT);
      setParams(nextParams, { replace: true });
      return { ...prev, project: '' };
    });
  }, [projects]);

  // A few pixels before a mouse drag begins, so the same element can be a link
  // and a card - tapping one opens the issue, dragging one moves it. A touch
  // instead waits out a press-and-hold, so a swipe scrolls rather than lifts.
  const sensors = useSensors(
    useSensor(MouseSensor, { activationConstraint: { distance: 5 } }),
    useSensor(TouchSensor, {
      activationConstraint: { delay: TOUCH_LIFT_DELAY_MS, tolerance: TOUCH_LIFT_TOLERANCE_PX },
    }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );

  const cards = board?.issues;
  const visible = useMemo(() => filterCards(cards ?? [], filter), [cards, filter]);
  const visibleKeys = useMemo(() => new Set(visible.map((c) => c.key)), [visible]);

  /* The board's own columns. The API sends every status - the issue page needs
     the deferred ones to offer them - and this is where they stop, so a parked
     column is never drawn and never a drop target. The cards sitting in one are
     dropped with it: there is no lane for them here, which is the point of
     shelving something, and they are still a click away by key or from their
     parent's Filed under list. */
  const columns = useMemo(() => boardColumns(board?.statuses ?? []), [board?.statuses]);

  /* How full the WIP section is, and which of the drawn columns are in it -
     read from board.wip and nothing else, so a column flagged isWip with no
     limit set draws no band at all. Named `section`, not `wip`: HA-92 binds
     that name to its own hook inside this component. */
  const section: Wip | null = board?.wip ?? null;
  const zone = useMemo(() => runs(columns, section), [columns, section]);
  const zoneIds = useMemo(() => new Set(zone.flatMap((run) => run.statusIds)), [zone]);

  /* The card a drop just sent into the section, held from onDragEnd until the
     reload that follows it lands - see the comment there. Set and cleared by
     onDragEnd alone, never by commit, since commit is shared with the peek's
     status picker and a press there has nothing to do with a drag's preview. */
  const [landing, setLanding] = useState<IssueCard | null>(null);

  // The counts and the tint the band and the lit columns draw: each slice's
  // own load, or one more for as long as a card of its type sits outside it.
  const loads = section ? preview(section, dragging ?? landing) : [];
  const tint: Tightness | null = section ? tightest(section, loads) : null;
  // The stacked layout's own header note (AC5): the one sentence WipBands
  // already draws across the run, read here for PhoneColumn instead of a
  // band it never draws.
  const wipText = section ? meterText(section, loads) : null;
  const previewing =
    section !== null &&
    dragging !== null &&
    section.slices.some((slice, i) => slice.limit !== null && loads[i] > slice.load);

  // Reads the pointer against the board's own ids rather than dnd-kit's
  // default corner-distance scoring - see lib/aim.ts for why that matters.
  const collisionDetection = useMemo(() => aimAt(board?.issues ?? []), [board?.issues]);

  /* Derived from the whole board rather than from what survives the filter,
     so choosing somebody does not empty the list you chose them from. */
  const assignees = useMemo(() => assigneeFacets(cards ?? []), [cards]);

  /**
   * One move path for a drop and a press: apply it optimistically, send it,
   * and raise what it earns - the close offer, and the chicklet with its
   * Undo. Shared so the peek's status picker gets everything a drag gets.
   *
   * `fromStatusId` is the peek's alone: sent, a stale board gets the server's
   * 409 sentence instead of silently overwriting a move the loop made while
   * the peek sat open. A drag does not send one - see docs/hatch.md,
   * *Rejected*, for why that stays a drag's own gesture.
   *
   * Throws on a refusal rather than catching it, so the two callers can
   * answer differently: `onDragEnd` shows it on the board, `send` reloads and
   * lets the peek show it instead. A WIP refusal is the one exception - it is
   * swallowed here, behind the dialog, so a drag and a peek press get it
   * uniformly rather than one getting a dialog and the other a bare inline
   * sentence.
   *
   * The retry reaches `commit` through `commitRef` rather than by name, so the
   * override call sits inside `commit`'s own body without reading the `const`
   * while it is still being assigned.
   */
  const commitRef = useRef<(placed: Placement, fromStatusId?: number, override?: boolean) => Promise<void>>(
    () => Promise.resolve(),
  );
  const commit = useCallback(
    async (placed: Placement, fromStatusId?: number, override = false) => {
      if (!board) return;

      // Read off every card rather than off `visible`, and before the repaint:
      // a card the filter is hiding is still work under this issue, one folded
      // off by a ready date is too, and this is the subtree as it stood when
      // the card was let go. Asked further down, once the move has come back.
      const was = board.issues.find((i) => i.key === placed.key)?.statusId ?? null;
      const offer = closeOffer(board, placed.key, was, placed.statusId);

      // Only a change of column is a transition worth confirming - the server
      // writes no event for a reorder either. What would put the card back is
      // read now, off the board as it stood: after the repaint below, the card
      // and its old neighbours are somewhere else.
      const move = dropConfirmation(board, placed.key, placed.statusId);
      if (offer) beforeClose.current.set(offer.key, cascadeEntries(board, offer));

      // Applied before the request so the card does not spring back under the
      // cursor for a round trip. A refusal reloads, which is the honest
      // correction: whatever the server thinks is what the board shows.
      setMoving((n) => n + 1);
      setBoard({ ...board, issues: placed.issues });

      const base = { statusId: placed.statusId, afterKey: placed.afterKey, beforeKey: placed.beforeKey, fromStatusId };
      const request = override ? overridden(base) : base;

      try {
        await moveIssue(placed.key, request);
        await reload();
        // After the reload, so the board the chicklet sits over already shows the move.
        if (move) moved(move);
        ask(offer);
      } catch (err) {
        const refusal = wipRefusal(err);
        if (refusal && !override) {
          await reload();
          wip.ask(placed.key, refusal, () => commitRef.current(placed, fromStatusId, true));
          return;
        }
        throw err;
      } finally {
        setMoving((n) => n - 1);
      }
    },
    [board, setBoard, reload, ask, moved, wip],
  );
  // Synced in an effect rather than during render, so a ref read is never a
  // render-time access - the retry only ever runs later, from a press.
  useEffect(() => {
    commitRef.current = commit;
  }, [commit]);

  const onDragEnd = useCallback(
    async (event: DragEndEvent) => {
      setDragging(null);
      setOver(null);
      if (!board) return;

      const placed = place(board.issues, visible, String(event.active.id), event.over ? String(event.over.id) : null);
      if (!placed) return;

      // Held past the repaint below and past the request, so the preview
      // does not go back to rest and then jump again once the reload's own
      // wip catches up - see lib/wip.ts's preview and the decision on the
      // ticket this implements.
      if (zoneIds.has(placed.statusId)) {
        setLanding(board.issues.find((i) => i.key === placed.key) ?? null);
      }

      try {
        await commit(placed);
      } catch (err) {
        // Nothing is asked here: a move the server refused closed nothing.
        setError(message(err));
        await reload();
      } finally {
        setLanding(null);
      }
    },
    [board, visible, commit, setError, reload, zoneIds],
  );

  /** The peek's status picker, sending a card to a column by a press rather
      than a drag. `fromStatusId` is the column the board currently holds the
      card in, so a peek left open on a board the loop has since moved the
      card on gets the server's refusal rather than overwriting it. */
  const send = useCallback(
    async (key: string, statusId: number) => {
      if (!board) return;
      const placed = sendTo(board.issues, key, statusId);
      if (!placed) return;

      const was = board.issues.find((i) => i.key === key)?.statusId;
      try {
        await commit(placed, was);
      } catch (err) {
        // Reloaded rather than shown here: this line is hidden behind the
        // peek, and the rethrow is what lets the dialog show it instead.
        await reload();
        throw err;
      }
    },
    [board, commit, reload],
  );

  // Undo on the board's own account: this listener is mounted only while the
  // board is the page on screen, so a keystroke made anywhere else cannot
  // rearrange it. Each condition below leaves the key to whoever else wants it -
  // a text field's own undo above all.
  const dragUnderway = dragging !== null;
  useEffect(() => {
    if (dragUnderway) return;

    const onKeyDown = (e: KeyboardEvent) => {
      if (!isUndoShortcut(e) || e.defaultPrevented) return;
      if (isTypingTarget(document.activeElement as HTMLElement | null)) return;
      // Every Modal marks itself so: a dialog is open, and it has the operator's attention.
      if (document.querySelector('[aria-modal="true"]')) return;

      if (undoNewest()) e.preventDefault();
    };

    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [dragUnderway, undoNewest]);

  /* `/` opens the console, on the same terms as Undo: not where text is being
     typed, not under a dialog (the console is one to everything else), not
     mid-drag. Mounted only while the board is the page on screen, which is what
     keeps it off every other page. preventDefault keeps the slash out of the
     prompt and Firefox's find bar shut. */
  const hasBoard = board != null;
  useEffect(() => {
    if (dragUnderway || !hasBoard) return;

    const onKeyDown = (e: KeyboardEvent) => {
      if (!isGoToShortcut(e) || e.defaultPrevented) return;
      if (isTypingTarget(document.activeElement as HTMLElement | null)) return;
      if (document.querySelector('[aria-modal="true"]')) return;

      e.preventDefault();
      setGoing(true);
    };

    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [dragUnderway, hasBoard]);

  /* A take, one render on: the fold has opened and the card is in the DOM. Focus
     it before the peek opens, so Modal records the card as its opener and gives
     focus back to it. A card a refresh took off the board in between still gets
     its peek. */
  useLayoutEffect(() => {
    if (!pending) return;

    const card = document.querySelector<HTMLElement>(`[data-issue-key="${CSS.escape(pending.key)}"]`);
    if (card) {
      const calm = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
      card.focus({ preventScroll: true });
      card.scrollIntoView({ block: 'center', inline: 'nearest', behavior: calm ? 'auto' : 'smooth' });
    }

    setPeeking(pending);
    setPending(null);
  }, [pending]);

  // An undo pressed anywhere moved a card this board is showing somewhere else.
  useEffect(() => onUndone(() => void reload()), [onUndone, reload]);

  if (error && !board) return <p className="text-danger">{error}</p>;
  if (!board) return <p className="text-muted">Loading…</p>;

  // The board as it now stands, for the card a peek was opened on: the peek
  // holds a snapshot, and the title, column and dates drawn should follow the
  // refresh. Keyed by key alone below, so the description is not fetched again.
  const peeked = peeking && (board.issues.find((i) => i.key === peeking.key) ?? peeking);

  // Marks the card and peeks it - or, where the card is not on the board to be
  // marked, peeks it and leaves the filter, the folds and the scroll alone.
  const onTake = (card: IssueCard) => {
    setGoing(false);
    stopLinger();
    if (whereOnBoard(card, visibleKeys, columns) === 'drawn') {
      setFound(card.key);
      setPending(card);
    } else {
      setFound(null);
      setPeeking(card);
    }
  };

  const onDragStart = ({ active }: DragStartEvent) => {
    stopLinger();
    setFound(null);
    setDragging(board.issues.find((card) => card.key === String(active.id)) ?? null);
  };

  const onDragOver = ({ over: target }: DragOverEvent) => {
    setOver(targetStatusId(target ? String(target.id) : null, board.issues));
  };

  const cancel = () => {
    setDragging(null);
    setOver(null);
  };

  return (
    <div className="hatch-board-page">
      {/* The page names itself here rather than through PageHeader: the top bar
          is deliberately not a page heading, and the board is the one page whose
          visible title the operator asked to lose. */}
      <h1 className="hatch-visually-hidden">Board</h1>

      {isPhone && (
        <Button variant="primary" className="hatch-board-filing" onClick={() => setFiling(true)}>
          New issue
        </Button>
      )}

      <BoardFilters
        filter={filter}
        onChange={changeFilter}
        assignees={assignees}
        projects={projects}
        cards={board.issues}
        showing={visible.length}
        total={board.issues.length}
        collapsed={isPhone}
        trailing={
          <>
            <QueueControl cards={board.issues} onTake={onTake} />
            {isPhone ? (
              <button
                type="button"
                className="hatch-board-view-toggle"
                onClick={() => {
                  const next: BoardView = stacked ? 'full' : 'stacked';
                  setView(next);
                  writeBoardView(next);
                }}
              >
                {stacked ? 'Full board' : 'Stacked view'}
              </button>
            ) : (
              <Button variant="primary" onClick={() => setFiling(true)}>
                New issue
              </Button>
            )}
          </>
        }
      />

      {error && <p className="text-danger">{error}</p>}

      {columns.length === 0 ? (
        <EmptyState message="This board has no columns yet." />
      ) : stacked ? (
        <DndContext
          sensors={sensors}
          collisionDetection={collisionDetection}
          onDragStart={onDragStart}
          onDragOver={onDragOver}
          onDragCancel={cancel}
          onDragEnd={(e) => void onDragEnd(e)}
        >
          <div className="hatch-board hatch-board--stacked">
            {columns.map((status) => (
              <PhoneColumn
                key={status.id}
                status={status}
                cards={visible.filter((i) => i.statusId === status.id)}
                hidden={board.issues.filter((i) => i.statusId === status.id).length - visible.filter((i) => i.statusId === status.id).length}
                filtering={isFiltering(filter)}
                found={found}
                wip={zoneIds.has(status.id) && tint !== null ? { text: wipText!, tint } : null}
                projectsByKey={projectsByKey}
                showProjectMark={showProjectMark}
              />
            ))}
          </div>

          <DragOverlay dropAnimation={null}>
            {dragging && (
              <CardPreview
                card={dragging}
                terminal={terminalOf(board.statuses, dragging.statusId)}
                project={showProjectMark ? (projectsByKey.get(dragging.projectKey) ?? null) : null}
              />
            )}
          </DragOverlay>
        </DndContext>
      ) : (
        <DndContext
          sensors={sensors}
          collisionDetection={collisionDetection}
          onDragStart={onDragStart}
          onDragOver={onDragOver}
          onDragCancel={cancel}
          onDragEnd={(e) => void onDragEnd(e)}
        >
          <div className={`hatch-board${zone.length ? ' hatch-board--wip' : ''}`}>
            <WipBands runs={zone} section={section} loads={loads} />
            {columns.map((status) => (
              <Column
                key={status.id}
                status={status}
                cards={visible.filter((i) => i.statusId === status.id)}
                hidden={board.issues.filter((i) => i.statusId === status.id).length - visible.filter((i) => i.statusId === status.id).length}
                filtering={isFiltering(filter)}
                dropping={dragging !== null && over === status.id}
                lit={previewing && over !== null && zoneIds.has(over) && zoneIds.has(status.id)}
                tint={zoneIds.has(status.id) ? tint : null}
                found={found}
                onPeek={peek}
                projectsByKey={projectsByKey}
                showProjectMark={showProjectMark}
              />
            ))}
          </div>

          {/* The card that follows the cursor. dnd-kit's sortable leaves the
              original in place and dims it, which by itself reads as a board
              that did not notice the drag - this is the half that moves. */}
          <DragOverlay dropAnimation={null}>
            {dragging && (
              <CardPreview
                card={dragging}
                terminal={terminalOf(board.statuses, dragging.statusId)}
                project={showProjectMark ? (projectsByKey.get(dragging.projectKey) ?? null) : null}
              />
            )}
          </DragOverlay>
        </DndContext>
      )}

      <NewIssueDialog
        open={filing}
        projects={projects}
        candidates={board.issues}
        defaultProjectKey={filter.project}
        onClose={() => setFiling(false)}
        onCreated={(created) => {
          // A card filed under a type the board is hiding would vanish from the
          // board it was filed on. `filter` is current: the dialog is modal.
          changeFilter(revealType(filter, created.type));
          void reload();
        }}
      />

      <IssuePeek
        card={peeked}
        status={peeked ? board.statuses.find((s) => s.id === peeked.statusId) : undefined}
        statuses={board.statuses}
        directory={directory}
        project={peeked && showProjectMark ? (projectsByKey.get(peeked.projectKey) ?? null) : null}
        onExpedited={() => void reload()}
        onMove={send}
        onClose={closePeek}
      />

      {going && (
        <OmniBar
          cards={board.issues}
          statuses={board.statuses}
          visibleKeys={visibleKeys}
          onTake={onTake}
          onClose={() => setGoing(false)}
        />
      )}

      <CloseSubtreeDialog
        offer={closing.offer}
        busy={closing.busy}
        error={closing.error}
        failures={closing.failures}
        onConfirm={closing.confirm}
        onClose={closing.close}
      />

      <WipOverrideDialog
        asking={wip.asking}
        busy={wip.busy}
        error={wip.error}
        onConfirm={wip.confirm}
        onClose={wip.close}
      />
    </div>
  );
}

const terminalOf = (statuses: Status[], statusId: number) =>
  statuses.find((s) => s.id === statusId)?.isTerminal ?? false;

/**
 * One column, and the fold that keeps it honest.
 *
 * An issue whose ready date has not arrived is real work that cannot be started
 * yet - next August's certificate renewal, filed the day the certificate was
 * bought. Left in place it pushes this week's work off the screen; deleted from
 * the view entirely it becomes a ticket nobody can find, which is a ticket
 * somebody files twice. So it is folded: counted in the header, one click away.
 *
 * The fold is the browser's alone. The API hands over every card (BoardDto), so
 * a script - or Claude - sees the whole board and does its own filtering.
 */
function Column({
  status,
  cards,
  hidden,
  filtering,
  dropping,
  lit,
  tint,
  found,
  onPeek,
  projectsByKey,
  showProjectMark,
}: {
  status: Status;
  cards: IssueCard[];
  /** How many of this column's cards the filter is holding back. */
  hidden: number;
  filtering: boolean;
  /** A drag is in progress and this is the column it would land in. */
  dropping: boolean;
  /** A counted card is being dragged over some column of this one's WIP
      section - every column in the section lights together, not just the one
      under the pointer. */
  lit: boolean;
  /** This column's tightness where it is in the WIP section, null where it
      is not. */
  tint: Tightness | null;
  /** The key of the card the console found, on any column. */
  found: string | null;
  onPeek: (card: IssueCard) => void;
  projectsByKey: Map<string, Project>;
  showProjectMark: boolean;
}) {
  const [showWaiting, setShowWaiting] = useState(false);

  // Its own droppable as well as a sortable context, and on the whole section
  // rather than just the cards area: aim.ts finds the target column by asking
  // which one the pointer is inside, and the heading and the empty space below
  // the last card have to answer that the same way the cards do, or a drop
  // over either reads as over no column at all.
  const { setNodeRef } = useDroppable({ id: columnDroppableId(status.id) });

  // Read once per render rather than per card, so a column cannot straddle
  // midnight and draw two different todays.
  const now = new Date();

  // Nothing is folded away in a terminal column. Work that shipped shipped,
  // whatever date was once on it, and hiding a done card behind "not ready yet"
  // would be the board arguing with what the operator just did.
  const waiting = status.isTerminal ? [] : cards.filter((c) => isWaiting(c.readyAt, now));
  const workable = cards.filter((c) => !waiting.includes(c));

  // The found card is folded away: open the fold, so there is a card to ring.
  // Adjusted during render, as IssuePeek does. It stays open once `found`
  // clears, because the fold is the operator's.
  if (found && !showWaiting && waiting.some((c) => c.key === found)) setShowWaiting(true);
  const shown = showWaiting ? [...workable, ...waiting] : workable;

  // Counted over `cards` and not over `shown`: the card that is owed an answer
  // is exactly the one that may be folded away behind `+N waiting`, and a
  // header that went quiet when the fold closed would hide the thing it exists
  // to point at.
  const asking = askingCount(cards);
  const askingWords = `${asking} card${asking === 1 ? '' : 's'} waiting on an answer`;

  return (
    <section
      className={`hatch-column${dropping || lit ? ' dropping' : ''}${tint ? ` hatch-column--wip hatch-wip-${tint}` : ''}`}
      style={statusVars(status.color)}
      ref={setNodeRef}
    >
      <header className="hatch-column-head">
        <StatusDot status={status} />
        <span className="hatch-column-name">{status.name}</span>
        <span className="hatch-column-count">{workable.length}</span>
        {asking > 0 && (
          <span className="hatch-column-asking" role="img" aria-label={askingWords} title={askingWords}>
            ?{asking}
          </span>
        )}
      </header>

      <div className="hatch-column-cards">
        {/* Only the cards actually drawn: dnd-kit sorts the ids it is given, and
            an id with nothing on screen behind it is a gap a drag falls into. */}
        <SortableContext items={shown.map((c) => c.key)} strategy={verticalListSortingStrategy}>
          {shown.map((card) => {
            const project = showProjectMark ? (projectsByKey.get(card.projectKey) ?? null) : null;
            return (
              <BoardCard
                key={card.key}
                card={card}
                waiting={waiting.includes(card)}
                terminal={status.isTerminal}
                found={found === card.key}
                onPeek={onPeek}
                project={project}
              />
            );
          })}
        </SortableContext>

        {waiting.length > 0 && (
          <button type="button" className="hatch-column-fold" onClick={() => setShowWaiting(!showWaiting)}>
            {showWaiting ? 'Hide' : `+ ${waiting.length}`} waiting
          </button>
        )}

        {/* Said out loud, because a short column and a filtered column look
            identical otherwise - and a card that is not where it was left is
            the fastest way to distrust a board. */}
        {filtering && hidden > 0 && <p className="hatch-column-hidden">{hidden} hidden by the filter</p>}
      </div>

      {/* The drop target, drawn in the column's own colour so the answer to
          "what am I moving this into" is the same colour as the column heading
          rather than a generic outline. */}
      {dropping && <div className="hatch-column-drop">{status.name}</div>}
    </section>
  );
}

/**
 * One column, stacked (AC1). The same `waiting`/`workable`/`shown`/`asking`
 * logic `Column` already has, copied rather than shared so the desk's own
 * render gets no new branch in it, with three differences the stacked layout
 * asks for: a `<details>` instead of a `<section>`, collapsible with a press
 * on its sticky `<summary>`; `open` seeded once from `startsCollapsed` and
 * held in its own state rather than re-derived every render, so a poll
 * landing while a column is open never re-closes it; and no
 * `useDroppable`/`dropping`/`lit` at all - there is no cross-column drop
 * target in this layout (AC3), only a reorder within one column's own
 * `SortableContext`.
 */
function PhoneColumn({
  status,
  cards,
  hidden,
  filtering,
  found,
  wip,
  projectsByKey,
  showProjectMark,
}: {
  status: Status;
  cards: IssueCard[];
  /** How many of this column's cards the filter is holding back. */
  hidden: number;
  filtering: boolean;
  /** The key of the card the console found, on any column. */
  found: string | null;
  /** This column's share of the WIP section's own sentence, or null where it
      is not in one - AC5: no band here, the header says it instead. */
  wip: { text: string; tint: Tightness } | null;
  projectsByKey: Map<string, Project>;
  showProjectMark: boolean;
}) {
  const [showWaiting, setShowWaiting] = useState(false);
  const [open, setOpen] = useState(() => !startsCollapsed(status));

  const now = new Date();

  const waiting = status.isTerminal ? [] : cards.filter((c) => isWaiting(c.readyAt, now));
  const workable = cards.filter((c) => !waiting.includes(c));

  if (found && !showWaiting && waiting.some((c) => c.key === found)) setShowWaiting(true);
  const shown = showWaiting ? [...workable, ...waiting] : workable;

  const asking = askingCount(cards);
  const askingWords = `${asking} card${asking === 1 ? '' : 's'} waiting on an answer`;

  return (
    <details
      className="hatch-column"
      style={statusVars(status.color)}
      open={open}
      onToggle={(e) => setOpen(e.currentTarget.open)}
    >
      <summary className="hatch-column-head">
        <StatusDot status={status} />
        <span className="hatch-column-name">{status.name}</span>
        <span className="hatch-column-count">{workable.length}</span>
        {asking > 0 && (
          <span className="hatch-column-asking" role="img" aria-label={askingWords} title={askingWords}>
            ?{asking}
          </span>
        )}
        {wip && <span className={`hatch-column-wip-note hatch-wip-${wip.tint}`}>{wip.text}</span>}
      </summary>

      <div className="hatch-column-cards">
        <SortableContext items={shown.map((c) => c.key)} strategy={verticalListSortingStrategy}>
          {shown.map((card) => {
            const project = showProjectMark ? (projectsByKey.get(card.projectKey) ?? null) : null;
            return (
              <BoardCard
                key={card.key}
                card={card}
                waiting={waiting.includes(card)}
                terminal={status.isTerminal}
                found={found === card.key}
                project={project}
              />
            );
          })}
        </SortableContext>

        {waiting.length > 0 && (
          <button type="button" className="hatch-column-fold" onClick={() => setShowWaiting(!showWaiting)}>
            {showWaiting ? 'Hide' : `+ ${waiting.length}`} waiting
          </button>
        )}

        {filtering && hidden > 0 && <p className="hatch-column-hidden">{hidden} hidden by the filter</p>}
      </div>
    </details>
  );
}
