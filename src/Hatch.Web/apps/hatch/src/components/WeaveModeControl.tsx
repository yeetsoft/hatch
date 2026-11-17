import { useEffect, useId, useRef, useState } from 'react';
import type { KeyboardEvent as ReactKeyboardEvent, MouseEvent as ReactMouseEvent } from 'react';
import { WEAVE_MODES, WEAVE_MODE_ROWS, weaveModeRow } from '../lib/dreams';
import { priorityLandingFocus } from '../lib/priority';
import type { WeaveMode } from '../types';

/**
 * The band's one button per mode, the current one marked and disabled - the
 * way `TypeSteps` is drawn bare, so a test can render the opened band without
 * a DOM. `landing` is the mode to focus once, on mount. Each step shows its
 * own name and its own hint as two lines, since a mode has no shape to carry
 * the word alone.
 */
export function WeaveModeSteps({
  current,
  landing,
  onPick,
}: {
  current: WeaveMode;
  landing?: WeaveMode | null;
  onPick: (mode: WeaveMode) => void;
}) {
  const buttons = useRef(new Map<WeaveMode, HTMLButtonElement>());

  useEffect(() => {
    if (landing == null) return;
    buttons.current.get(landing)?.focus();
    // Once, on mount: a focus request is for the moment the band opens, not
    // for every re-render while it stays open.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div className="hatch-weave-mode-steps" role="group" aria-label="Set mode">
      {WEAVE_MODES.map((name) => {
        const row = weaveModeRow(name);
        const here = name === current;
        return (
          <button
            key={name}
            ref={(el) => {
              if (el) buttons.current.set(name, el);
              else buttons.current.delete(name);
            }}
            type="button"
            className={`hatch-weave-mode-step${here ? ' here' : ''}`}
            aria-pressed={here}
            disabled={here}
            onClick={() => onPick(name)}
          >
            <span className="hatch-weave-mode-step__name">{row.word}</span>
            <span className="hatch-weave-mode-step__hint">{row.hint}</span>
          </button>
        );
      })}
    </div>
  );
}

/**
 * The mode pill, and the band it opens onto: it replaces the native Mode
 * dropdown on the Dreams page. Shaped after `TypeControl` - a pill that
 * wears the current mode's name, pressed, opens a band of one step per mode,
 * the current one marked - cut down for a control that holds its own state
 * and is never mid-save: no `issueKey`, no `label`, no `busy`.
 *
 * A press folds the band, returns focus to the pill, and only then calls
 * `onChange`. Escape folds the band and stops there, before it can reach a
 * dialog's own listener.
 */
export function WeaveModeControl({ mode, onChange }: { mode: WeaveMode; onChange: (mode: WeaveMode) => void }) {
  const [open, setOpen] = useState(false);
  const [landing, setLanding] = useState<WeaveMode | null | undefined>(undefined);
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
    setLanding(event.detail === 0 ? priorityLandingFocus(WEAVE_MODE_ROWS, mode) : undefined);
    setOpen(true);
  };

  const handleChange = (next: WeaveMode) => {
    fold();
    onChange(next);
  };

  const row = weaveModeRow(mode);
  const label = `Mode: ${row.word}. ${row.hint}`;

  return (
    <>
      <button
        ref={buttonRef}
        type="button"
        className="hatch-btn hatch-gradient-fill hatch-weave-mode-pill-press"
        aria-expanded={open}
        aria-controls={bandId}
        aria-label={label}
        title={label}
        onClick={onTriggerClick}
        onKeyDown={onEscape}
      >
        {row.word}
        <svg className="hatch-weave-mode-pill-caret" viewBox="0 0 12 12" width="12" height="12" aria-hidden="true">
          <path d="M2.5 4.5 6 8l3.5-3.5" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      </button>

      {open && (
        <div id={bandId} className="hatch-weave-mode-pill-band" onKeyDown={onEscape}>
          <span className="hatch-status-bar-label">Set mode</span>
          <WeaveModeSteps current={mode} landing={landing} onPick={handleChange} />
        </div>
      )}
    </>
  );
}
