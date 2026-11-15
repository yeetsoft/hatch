import { Fragment, useEffect, useRef } from 'react';
import { statusVars } from '../lib/color';
import { statusChoices } from '../lib/columns';
import type { Status } from '../types';

/**
 * Every column, one press each - the issue page's status bar and the board
 * peek's status picker draw the same group, so the two screens cannot
 * disagree about which columns are offered, in what order, or how the shelf
 * is set apart.
 *
 * Every column is offered, in board order, because Hatch has no transition
 * rules on purpose (docs/hatch.md, "Non-goals") - any status to any status,
 * we trust ourselves.
 *
 * Including the deferred ones, which is what makes this the only way onto the
 * shelf from a status bar, and the peek's band the second. The board cannot
 * offer them as a drop target - a column there is a place a drag can overshoot
 * into - so they are drawn here, after a divider, as the presses they are: a
 * decision about this ticket, made deliberately.
 */
export function StatusSteps({
  statuses,
  statusId,
  onMove,
  onRemand,
  issueKey,
  focus,
  blocked,
}: {
  statuses: Status[];
  statusId: number;
  onMove: (statusId: number) => void;
  /** Remand this issue to a dream in the given deferred column. Left off, or
      with no deferred column, no second press is drawn beside the shelf. */
  onRemand?: (statusId: number) => void;
  /** The issue, for the second press's label. */
  issueKey?: string;
  /** The column to focus once this mounts - the keyboard's landing spot when
      a picker opens. Left off, nothing is focused. */
  focus?: number | null;
  /** Why a move into this column is refused, or null where it is allowed.
      Left off, nothing is blocked - today's behaviour. */
  blocked?: (status: Status) => string | null;
}) {
  const { lanes, shelf } = statusChoices(statuses);
  const buttons = useRef(new Map<number, HTMLButtonElement>());

  useEffect(() => {
    if (focus == null) return;
    buttons.current.get(focus)?.focus();
    // Once, on mount: a focus request is for the moment the band opens, not
    // for every re-render while it stays open.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const step = (status: Status) => {
    const here = status.id === statusId;
    const reason = blocked?.(status) ?? null;
    return (
      <button
        key={status.id}
        ref={(el) => {
          if (el) buttons.current.set(status.id, el);
          else buttons.current.delete(status.id);
        }}
        type="button"
        className={`hatch-status-step${here ? ' here' : ''}${status.isDeferred ? ' deferred' : ''}${reason ? ' blocked' : ''}`}
        style={statusVars(status.color)}
        aria-pressed={here}
        disabled={here || reason !== null}
        title={reason ?? undefined}
        onClick={() => onMove(status.id)}
      >
        {status.name}
      </button>
    );
  };

  // The second press beside a deferred column: the same destination, by way
  // of the dialog that posts a dream from the issue and what is open under it.
  // It is not disabled on the column the issue is already in - the route takes
  // a root already there - but it copies the move's refusal, so the two
  // presses agree about a protected column.
  const remand = (status: Status) => {
    const reason = blocked?.(status) ?? null;
    const label = `Remand ${issueKey ?? 'this issue'} to a dream in ${status.name}`;
    return (
      <button
        key={`remand-${status.id}`}
        type="button"
        className={`hatch-status-step hatch-status-remand${reason ? ' blocked' : ''}`}
        style={statusVars(status.color)}
        disabled={reason !== null}
        title={reason ?? undefined}
        aria-label={label}
        onClick={() => onRemand?.(status.id)}
      >
        to a dream
      </button>
    );
  };

  return (
    <div className="hatch-status-steps" role="group" aria-label="Move this issue">
      {lanes.map(step)}

      {/* Grouped and labelled rather than run on to the end of the row,
          because these do not continue the board - they leave it. A board
          with no deferred column draws neither the divider nor the label and
          reads exactly as it did before. */}
      {shelf.length > 0 && (
        <>
          <span className="hatch-status-shelf-label" aria-hidden="true">
            or park it
          </span>
          {shelf.map((status) => (
            <Fragment key={status.id}>
              {step(status)}
              {onRemand && remand(status)}
            </Fragment>
          ))}
        </>
      )}
    </div>
  );
}
