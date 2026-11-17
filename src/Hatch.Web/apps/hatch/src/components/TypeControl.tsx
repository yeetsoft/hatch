import { useEffect, useId, useRef, useState } from 'react';
import type { KeyboardEvent as ReactKeyboardEvent, MouseEvent as ReactMouseEvent } from 'react';
import { issueTypeRow } from '../lib/issueType';
import { priorityLandingFocus } from '../lib/priority';
import { ISSUE_TYPES } from '../types';
import type { IssueType } from '../types';
import { TypeShape } from './TypeGlyph';

/**
 * The band's one button per type, the own type marked and disabled - the way
 * `OnePrSteps` is drawn bare, so a test can render the opened band without a
 * DOM. `landing` is the type to focus once, on mount.
 */
export function TypeSteps({
  current,
  types = ISSUE_TYPES,
  landing,
  onPick,
}: {
  current: IssueType;
  types?: readonly IssueType[];
  landing?: IssueType | null;
  onPick: (type: IssueType) => void;
}) {
  const buttons = useRef(new Map<IssueType, HTMLButtonElement>());

  useEffect(() => {
    if (landing == null) return;
    buttons.current.get(landing)?.focus();
    // Once, on mount: a focus request is for the moment the band opens, not
    // for every re-render while it stays open.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  if (types.length === 0) return null;

  return (
    <div className="hatch-type-pill-steps" role="group" aria-label="Set type">
      {types.map((name) => {
        const row = issueTypeRow(name);
        const here = name === current;
        return (
          <button
            key={name}
            ref={(el) => {
              if (el) buttons.current.set(name, el);
              else buttons.current.delete(name);
            }}
            type="button"
            className={`hatch-type-pill-step ${row.class}${here ? ' here' : ''}`}
            title={row.title}
            aria-pressed={here}
            disabled={here}
            onClick={() => onPick(name)}
          >
            <span className="hatch-type-pill-glyph" aria-hidden="true">
              <TypeShape type={name} />
            </span>
            {row.word}
          </button>
        );
      })}
    </div>
  );
}

/**
 * The type pill, and the band it opens onto: it replaces the native dropdown
 * in the Details card's edit mode. Shaped after `PriorityControl` - a pill
 * that wears the current type's shape, word and colour and, pressed, opens a
 * band of one step per type, the current one marked.
 *
 * A press folds the band, returns focus to the pill, and only then calls
 * `onChange`. Escape folds the band and stops there, before it can reach a
 * dialog's own listener.
 *
 * Presentational: the caller saves, and a refusal lands in the page's own
 * error line in the server's own words.
 */
export function TypeControl({
  issueKey,
  type,
  types = ISSUE_TYPES,
  busy = false,
  onChange,
}: {
  issueKey: string;
  type: IssueType;
  /** The types the band offers. The pill wears `type` even when it is not one of them. */
  types?: readonly IssueType[];
  /** A save is already out: the pill stays where it is and stops answering. */
  busy?: boolean;
  onChange: (type: IssueType) => void;
}) {
  const [open, setOpen] = useState(false);
  const [landing, setLanding] = useState<IssueType | null | undefined>(undefined);
  const bandId = useId();
  const buttonRef = useRef<HTMLButtonElement>(null);

  const fold = () => {
    setOpen(false);
    buttonRef.current?.focus();
  };

  const onEscape = (event: ReactKeyboardEvent) => {
    if (event.key !== 'Escape' || !open) return;
    event.stopPropagation();
    fold();
  };

  const onTriggerClick = (event: ReactMouseEvent<HTMLButtonElement>) => {
    if (open) {
      fold();
      return;
    }
    // detail is 0 for a keyboard-initiated click (Enter, Space), the one case
    // that should land focus inside the band.
    setLanding(event.detail === 0 ? priorityLandingFocus(types.map((name) => ({ name })), type) : undefined);
    setOpen(true);
  };

  const handleChange = (next: IssueType) => {
    fold();
    onChange(next);
  };

  const row = issueTypeRow(type);
  const label = `Type: ${row.word}. Set ${issueKey}'s type`;

  return (
    <>
      <button
        ref={buttonRef}
        type="button"
        className={`hatch-type-pill hatch-type-pill-press ${row.class}`}
        aria-expanded={open}
        aria-controls={bandId}
        aria-busy={busy}
        aria-label={label}
        title={label}
        disabled={busy}
        onClick={onTriggerClick}
        onKeyDown={onEscape}
      >
        <span className="hatch-type-pill-glyph" aria-hidden="true">
          <TypeShape type={type} />
        </span>
        {row.word}
        <svg className="hatch-type-pill-caret" viewBox="0 0 12 12" width="12" height="12" aria-hidden="true">
          <path d="M2.5 4.5 6 8l3.5-3.5" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      </button>

      {open && (
        <div id={bandId} className={`hatch-type-pill-band ${row.class}`} onKeyDown={onEscape}>
          <span className="hatch-status-bar-label">Set type</span>
          <TypeSteps current={type} types={types} landing={landing} onPick={handleChange} />
        </div>
      )}
    </>
  );
}
