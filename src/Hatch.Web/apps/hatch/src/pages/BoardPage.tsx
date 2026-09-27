import { useCallback, useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  DndContext,
  DragOverlay,
  KeyboardSensor,
  PointerSensor,
  useDroppable,
  useSensor,
  useSensors,
} from '@dnd-kit/core';
import type { DragEndEvent, DragOverEvent, DragStartEvent } from '@dnd-kit/core';
import { SortableContext, sortableKeyboardCoordinates, verticalListSortingStrategy } from '@dnd-kit/sortable';
import { Button, EmptyState, PageHeader } from '@hatch/ui';
import { getAssignees, getBoard, getProjects, moveIssue } from '../api/client';
import { BoardCard, CardPreview } from '../components/BoardCard';
import { BoardFilters } from '../components/BoardFilters';
import { CloseSubtreeDialog } from '../components/CloseSubtreeDialog';
import { IssuePeek } from '../components/IssuePeek';
import { NewIssueDialog } from '../components/NewIssueDialog';
import { StatusDot } from '../components/StatusPill';
import { statusVars } from '../lib/color';
import { closeOffer } from '../lib/closeSubtree';
import { boardColumns } from '../lib/columns';
import { message } from '../lib/errors';
import { NO_FILTER, assigneeFacets, filterCards, isFiltering } from '../lib/filter';
import type { CardFilter } from '../lib/filter';
import { aimAt } from '../lib/aim';
import { columnDroppableId, place, targetStatusId } from '../lib/place';
import { askingCount } from '../lib/questions';
import { isWaiting } from '../lib/schedule';
import { useCloseSubtree } from '../lib/useCloseSubtree';
import { useLoaded } from '../lib/useLoaded';
import type { AssigneeDirectory, Board, IssueCard, Project, Status } from '../types';

/** The URL's whole vocabulary here: which project, by key - the same param
    PlanPage's picker uses. */
const PROJECT = 'project';

export function BoardPage() {
  const { data: board, setData: setBoard, error, setError, reload } = useLoaded<Board>(getBoard);
  const [projects, setProjects] = useState<Project[]>([]);
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
  const [filter, setFilter] = useState<CardFilter>(() => ({ ...NO_FILTER, project: params.get(PROJECT) ?? '' }));

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

  // The card under the cursor and the column it is over, kept only for the
  // duration of a drag: one paints the overlay, the other lights up the column
  // the drop would land in.
  const [dragging, setDragging] = useState<IssueCard | null>(null);
  const [over, setOver] = useState<number | null>(null);

  /** The card a click opened a summary for. Null when the dialog is closed. */
  const [peeking, setPeeking] = useState<IssueCard | null>(null);

  // The offer to close everything under a card dropped into a terminal column.
  const closing = useCloseSubtree(reload);
  const { ask } = closing;

  useEffect(() => {
    getProjects()
      .then((ps) => {
        setProjects(ps);
        // A key in the URL that names no project reads as all projects, and
        // the param is dropped - the picker has nothing to show it as chosen.
        setFilter((prev) => {
          if (prev.project === '' || ps.some((p) => p.key === prev.project)) return prev;
          const nextParams = new URLSearchParams(params);
          nextParams.delete(PROJECT);
          setParams(nextParams, { replace: true });
          return { ...prev, project: '' };
        });
      })
      .catch(() => {
        // The board is readable without the project list; only the New issue
        // dialog needs it, and it says so itself when there is nothing to pick.
      });

    getAssignees().then(setDirectory).catch(() => {
      // And without the directory: every card still draws, and the summary
      // says whether an issue is expedited without offering to change it.
    });
  }, []);

  // A few pixels before a drag begins, so the same element can be a link and a
  // card - tapping one opens the issue, dragging one moves it.
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 5 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );

  const cards = board?.issues;
  const visible = useMemo(() => filterCards(cards ?? [], filter), [cards, filter]);

  /* The board's own columns. The API sends every status - the issue page needs
     the deferred ones to offer them - and this is where they stop, so a parked
     column is never drawn and never a drop target. The cards sitting in one are
     dropped with it: there is no lane for them here, which is the point of
     shelving something, and they are still a click away by key or from their
     parent's Filed under list. */
  const columns = useMemo(() => boardColumns(board?.statuses ?? []), [board?.statuses]);

  // Reads the pointer against the board's own ids rather than dnd-kit's
  // default corner-distance scoring - see lib/aim.ts for why that matters.
  const collisionDetection = useMemo(() => aimAt(board?.issues ?? []), [board?.issues]);

  /* Derived from the whole board rather than from what survives the filter,
     so choosing somebody does not empty the list you chose them from. */
  const assignees = useMemo(() => assigneeFacets(cards ?? []), [cards]);

  const onDragEnd = useCallback(
    async (event: DragEndEvent) => {
      setDragging(null);
      setOver(null);
      if (!board) return;

      const placed = place(board.issues, visible, String(event.active.id), event.over ? String(event.over.id) : null);
      if (!placed) return;

      // Read off every card rather than off `visible`, and before the repaint:
      // a card the filter is hiding is still work under this issue, one folded
      // off by a ready date is too, and this is the subtree as it stood when
      // the card was let go. Asked further down, once the move has come back.
      const was = board.issues.find((i) => i.key === placed.key)?.statusId ?? null;
      const offer = closeOffer(board, placed.key, was, placed.statusId);

      // Applied before the request so the card does not spring back under the
      // cursor for a round trip. A refusal reloads, which is the honest
      // correction: whatever the server thinks is what the board shows.
      setBoard({ ...board, issues: placed.issues });

      try {
        await moveIssue(placed.key, {
          statusId: placed.statusId,
          afterKey: placed.afterKey,
          beforeKey: placed.beforeKey,
        });
        await reload();
        ask(offer);
      } catch (err) {
        // Nothing is asked here: a move the server refused closed nothing.
        setError(message(err));
        await reload();
      }
    },
    [board, visible, setBoard, setError, reload, ask],
  );

  if (error && !board) return <p className="text-danger">{error}</p>;
  if (!board) return <p className="text-muted">Loading…</p>;

  const onDragStart = ({ active }: DragStartEvent) => {
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
      <PageHeader
        title="Board"
        actions={
          <Button variant="primary" onClick={() => setFiling(true)}>
            New issue
          </Button>
        }
      />

      <BoardFilters
        filter={filter}
        onChange={changeFilter}
        assignees={assignees}
        projects={projects}
        showing={visible.length}
        total={board.issues.length}
      />

      {error && <p className="text-danger">{error}</p>}

      {columns.length === 0 ? (
        <EmptyState message="This board has no columns yet." />
      ) : (
        <DndContext
          sensors={sensors}
          collisionDetection={collisionDetection}
          onDragStart={onDragStart}
          onDragOver={onDragOver}
          onDragCancel={cancel}
          onDragEnd={(e) => void onDragEnd(e)}
        >
          <div className="hatch-board">
            {columns.map((status) => (
              <Column
                key={status.id}
                status={status}
                cards={visible.filter((i) => i.statusId === status.id)}
                hidden={board.issues.filter((i) => i.statusId === status.id).length - visible.filter((i) => i.statusId === status.id).length}
                filtering={isFiltering(filter)}
                dropping={dragging !== null && over === status.id}
                onPeek={setPeeking}
              />
            ))}
          </div>

          {/* The card that follows the cursor. dnd-kit's sortable leaves the
              original in place and dims it, which by itself reads as a board
              that did not notice the drag - this is the half that moves. */}
          <DragOverlay dropAnimation={null}>
            {dragging && <CardPreview card={dragging} terminal={terminalOf(board.statuses, dragging.statusId)} />}
          </DragOverlay>
        </DndContext>
      )}

      <NewIssueDialog
        open={filing}
        projects={projects}
        defaultProjectKey={filter.project}
        onClose={() => setFiling(false)}
        onCreated={() => void reload()}
      />

      <IssuePeek
        card={peeking}
        status={peeking ? board.statuses.find((s) => s.id === peeking.statusId) : undefined}
        directory={directory}
        onExpedited={() => void reload()}
        onClose={() => setPeeking(null)}
      />

      <CloseSubtreeDialog
        offer={closing.offer}
        busy={closing.busy}
        error={closing.error}
        failures={closing.failures}
        onConfirm={closing.confirm}
        onClose={closing.close}
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
  onPeek,
}: {
  status: Status;
  cards: IssueCard[];
  /** How many of this column's cards the filter is holding back. */
  hidden: number;
  filtering: boolean;
  /** A drag is in progress and this is the column it would land in. */
  dropping: boolean;
  onPeek: (card: IssueCard) => void;
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
  const shown = showWaiting ? [...workable, ...waiting] : workable;

  // Counted over `cards` and not over `shown`: the card that is owed an answer
  // is exactly the one that may be folded away behind `+N waiting`, and a
  // header that went quiet when the fold closed would hide the thing it exists
  // to point at.
  const asking = askingCount(cards);
  const askingWords = `${asking} card${asking === 1 ? '' : 's'} waiting on an answer`;

  return (
    <section className={`hatch-column${dropping ? ' dropping' : ''}`} style={statusVars(status.color)} ref={setNodeRef}>
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
          {shown.map((card) => (
            <BoardCard
              key={card.key}
              card={card}
              waiting={waiting.includes(card)}
              terminal={status.isTerminal}
              onPeek={onPeek}
            />
          ))}
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
