import { useId, useRef, useState } from 'react';
import type { KeyboardEvent as ReactKeyboardEvent, MouseEvent as ReactMouseEvent } from 'react';
import { statusVars } from '../lib/color';
import { landingFocus } from '../lib/columns';
import { StatusSteps } from './StatusSteps';
import type { Status } from '../types';

/**
 * The peek's status pill, and the band it opens onto: the board's own way to
 * move a card without closing the dialog and dragging it.
 *
 * The pill wears the same classes `StatusPill` draws, plus a caret and the
 * chrome that makes it a button - so at rest it is pixel for pixel the pill
 * that was there before. Pressed, a band unfolds under it holding `StatusSteps`
 * in miniature - the issue page's own column list, so the two screens can never
 * disagree about what is offered.
 *
 * `open` is its only state. The caller mounts this with `key={card.key}`, so
 * opening the peek on a different card always starts folded rather than
 * carrying the last card's band open or its last refusal into view.
 *
 * A press folds the band, returns focus to the pill, and only then calls
 * `onMove` - so the repaint the caller triggers never fights a band still
 * reading its own now-stale `statusId`. Escape is handled on both the pill and
 * the band, stopping propagation before it reaches Modal's document listener -
 * the same pattern `Menu.tsx` uses to keep one Escape from doing two things at
 * once.
 */
export function StatusPicker({
  issueKey,
  status,
  statuses,
  busy,
  onMove,
}: {
  issueKey: string;
  /** The column the card is sitting in. */
  status: Status;
  /** Every column, deferred ones included - `board.statuses`. */
  statuses: Status[];
  /** A move for this card is already out: the pill cannot be pressed again
      until the server has answered it. */
  busy: boolean;
  onMove: (statusId: number) => void;
}) {
  const [open, setOpen] = useState(false);
  const [landing, setLanding] = useState<number | null | undefined>(undefined);
  const bandId = useId();
  const buttonRef = useRef<HTMLButtonElement>(null);

  const fold = () => {
    setOpen(false);
    buttonRef.current?.focus();
  };

  const onEscape = (event: ReactKeyboardEvent) => {
    if (event.key !== 'Escape' || !open) return;
    // Stops the native event at the root before Modal's document listener
    // sees it, so Escape closes only this band and not the peek behind it.
    event.stopPropagation();
    fold();
  };

  const onTriggerClick = (event: ReactMouseEvent<HTMLButtonElement>) => {
    if (open) {
      fold();
      return;
    }
    // detail is 0 for a keyboard-initiated click (Enter, Space), which is the
    // one case that should land focus inside the band. A pointer click leaves
    // focus on the pill, which already has it.
    setLanding(event.detail === 0 ? landingFocus(statuses, status.id) : undefined);
    setOpen(true);
  };

  const handleMove = (statusId: number) => {
    fold();
    onMove(statusId);
  };

  return (
    <>
      <button
        ref={buttonRef}
        type="button"
        className="hatch-status-pill hatch-status-pill-sm hatch-status-pill-press"
        style={statusVars(status.color)}
        aria-expanded={open}
        aria-controls={bandId}
        aria-busy={busy}
        aria-label={`Status: ${status.name}. Move ${issueKey}`}
        disabled={busy}
        onClick={onTriggerClick}
        onKeyDown={onEscape}
      >
        {status.name}
        <svg className="hatch-status-pill-caret" viewBox="0 0 12 12" width="12" height="12" aria-hidden="true">
          <path d="M2.5 4.5 6 8l3.5-3.5" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      </button>

      {open && (
        <div id={bandId} className="hatch-peek-status" style={statusVars(status.color)} onKeyDown={onEscape}>
          <span className="hatch-status-bar-label">Move to</span>
          <StatusSteps statuses={statuses} statusId={status.id} onMove={handleMove} focus={landing} />
        </div>
      )}
    </>
  );
}
