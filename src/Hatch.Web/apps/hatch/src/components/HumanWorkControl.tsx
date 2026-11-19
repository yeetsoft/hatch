import { useEffect, useId, useRef, useState } from 'react';
import type { KeyboardEvent as ReactKeyboardEvent, MouseEvent as ReactMouseEvent } from 'react';
import { HUMAN_WORK_VALUES } from '../lib/humanWork';
import type { HumanWork, HumanWorkValue } from '../lib/humanWork';
import { priorityLandingFocus } from '../lib/priority';
import type { AssigneeDirectory } from '../types';

/** What this file and its importers import the type from. */
export type { HumanWork };

const VALUES = HUMAN_WORK_VALUES;
const INHERIT = VALUES.find((value) => value.name === 'inherit')!;
const valueOf = (name: HumanWork): HumanWorkValue => VALUES.find((value) => value.name === name) ?? INHERIT;

/** The band's one button per value - `LocalModelBand` without the level
    table, since human work has three names rather than three steps. The own
    value (`current`) is the marked, disabled step. */
export function HumanWorkBand({
  current,
  landing,
  onPick,
}: {
  current: HumanWork;
  landing: HumanWork | null | undefined;
  onPick: (value: HumanWork) => void;
}) {
  const buttons = useRef(new Map<HumanWork, HTMLButtonElement>());

  useEffect(() => {
    if (landing == null) return;
    buttons.current.get(landing)?.focus();
    // Once, on mount: a focus request is for the moment the band opens, not
    // for every re-render while it stays open.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div className="hatch-humanwork-steps" role="group" aria-label="Set human work">
      {VALUES.map((value) => {
        const here = value.name === current;
        return (
          <button
            key={value.name}
            ref={(el) => {
              if (el) buttons.current.set(value.name, el);
              else buttons.current.delete(value.name);
            }}
            type="button"
            className={`hatch-humanwork-step${value.class ? ` ${value.class}` : ''}${here ? ' here' : ''}`}
            title={value.title}
            aria-pressed={here}
            disabled={here}
            onClick={() => onPick(value.name)}
          >
            {value.glyph && (
              <span aria-hidden="true">
                {value.glyph}
                {' '}
              </span>
            )}
            {value.word}
          </button>
        );
      })}
    </div>
  );
}

/**
 * The human-work pill, and the band it opens onto: who works this issue,
 * drawn the same shape as `LocalModelControl` for the same reason - one
 * press names exactly the value it sets.
 *
 * A pill that at rest wears the effective value (`humanWork`, human or
 * agent) and, pressed, opens a band holding one step per `VALUES` entry, the
 * issue's own value (`humanWorkOwn`) marked. `open` and `landing` are its
 * only state, so the caller mounting this fresh per issue (`key={issue.key}`)
 * always starts folded.
 *
 * A press folds the band, returns focus to the pill, and only then calls
 * `onChange`, so the repaint the caller triggers never fights a band still
 * reading a stale value. Escape is handled on the pill and the band, stopping
 * propagation before it reaches Modal's document listener.
 *
 * Presentational and fetching nothing: a refusal lands in the page's own
 * error line in the server's own words, and this never has an opinion about
 * whether a press worked.
 */
export function HumanWorkControl({
  issueKey,
  humanWork,
  humanWorkOwn,
  humanWorkFrom,
  directory,
  busy = false,
  size = 'md',
  onChange,
}: {
  issueKey: string;
  /** The value in effect - what the pill wears. Always `human` or `agent`. */
  humanWork: HumanWork;
  /** This issue's own value - the step the band marks as current. */
  humanWorkOwn: HumanWork;
  /** The ancestor the effective value came from, or null when it is this
      issue's own, or nothing is set. */
  humanWorkFrom: string | null;
  /** Everybody who could own an issue, and who the caller is. Null while it is
      still loading. */
  directory: AssigneeDirectory | null;
  /** A press is already out: the pill stays where it is and stops answering. */
  busy?: boolean;
  /** `lg` is the issue page's status bar, drawn the size of its status pill. */
  size?: 'md' | 'lg';
  onChange: (value: HumanWork) => void;
}) {
  const [open, setOpen] = useState(false);
  const [landing, setLanding] = useState<HumanWork | null | undefined>(undefined);
  const bandId = useId();
  const buttonRef = useRef<HTMLButtonElement>(null);

  const fold = () => {
    setOpen(false);
    buttonRef.current?.focus();
  };

  const onEscape = (event: ReactKeyboardEvent) => {
    if (event.key !== 'Escape' || !open) return;
    // Stops the native event at the root before Modal's document listener
    // sees it, so Escape closes only this band.
    event.stopPropagation();
    fold();
  };

  const onTriggerClick = (event: ReactMouseEvent<HTMLButtonElement>) => {
    if (open) {
      fold();
      return;
    }
    // detail is 0 for a keyboard-initiated click (Enter, Space), which is the
    // one case that should land focus inside the band.
    setLanding(event.detail === 0 ? priorityLandingFocus(VALUES, humanWorkOwn) : undefined);
    setOpen(true);
  };

  const handleChange = (value: HumanWork) => {
    fold();
    onChange(value);
  };

  const effective = valueOf(humanWork);

  /* `kind === 'person'`: the write is closed to an API key, so a key holding
     this page would be offered a press that could only be refused. It still
     gets the state, drawn as a word rather than as a dead button. */
  if (directory?.me?.kind !== 'person') {
    return (
      <span className={`hatch-humanwork-said${effective.class ? ` ${effective.class}` : ''}`}>
        {effective.word}
        {humanWorkFrom ? ` · inherited from ${humanWorkFrom}` : ''}
      </span>
    );
  }

  const label = `Human work: ${effective.word}${humanWorkFrom ? `, inherited from ${humanWorkFrom}` : ''}. Set ${issueKey}'s human-work override`;

  return (
    <>
      <button
        ref={buttonRef}
        type="button"
        className={`hatch-humanwork-pill hatch-humanwork-pill-press${size === 'lg' ? ' hatch-humanwork-pill-lg' : ''}${effective.class ? ` ${effective.class}` : ''}`}
        aria-expanded={open}
        aria-controls={bandId}
        aria-busy={busy}
        aria-label={label}
        title={label}
        disabled={busy}
        onClick={onTriggerClick}
        onKeyDown={onEscape}
      >
        {effective.glyph && (
          <span aria-hidden="true">
            {effective.glyph}
            {' '}
          </span>
        )}
        {effective.word}
        <svg className="hatch-humanwork-pill-caret" viewBox="0 0 12 12" width="12" height="12" aria-hidden="true">
          <path d="M2.5 4.5 6 8l3.5-3.5" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      </button>

      {open && (
        <div
          id={bandId}
          className={`hatch-humanwork-band${effective.class ? ` ${effective.class}` : ''}`}
          onKeyDown={onEscape}
        >
          <span className="hatch-status-bar-label">Set human work</span>
          <HumanWorkBand current={humanWorkOwn} landing={landing} onPick={handleChange} />
        </div>
      )}
    </>
  );
}
