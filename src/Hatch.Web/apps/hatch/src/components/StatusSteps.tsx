import { useEffect, useRef } from 'react';
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
  focus,
}: {
  statuses: Status[];
  statusId: number;
  onMove: (statusId: number) => void;
  /** The column to focus once this mounts - the keyboard's landing spot when
      a picker opens. Left off, nothing is focused. */
  focus?: number | null;
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
    return (
      <button
        key={status.id}
        ref={(el) => {
          if (el) buttons.current.set(status.id, el);
          else buttons.current.delete(status.id);
        }}
        type="button"
        className={`hatch-status-step${here ? ' here' : ''}${status.isDeferred ? ' deferred' : ''}`}
        style={statusVars(status.color)}
        aria-pressed={here}
        disabled={here}
        onClick={() => onMove(status.id)}
      >
        {status.name}
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
          {shelf.map(step)}
        </>
      )}
    </div>
  );
}
