import { useEffect, useId, useRef, useState } from 'react';
import type { KeyboardEvent as ReactKeyboardEvent, MouseEvent as ReactMouseEvent } from 'react';
import { LOCAL_MODEL_LEVELS } from '../lib/localModel';
import type { LocalModel, LocalModelLevel } from '../lib/localModel';
import { priorityLandingFocus } from '../lib/priority';
import type { AssigneeDirectory } from '../types';

/** What this file and its importers import the type from. */
export type { LocalModel };

const LEVELS = LOCAL_MODEL_LEVELS;
const NONE = LEVELS.find((level) => level.name === 'none')!;
const levelOf = (name: LocalModel): LocalModelLevel => LEVELS.find((level) => level.name === name) ?? NONE;

/** The band's one button per level - `PreApprovalBand` without the approver
    lock, since any person may set this. The own level (`current`) is the
    marked, disabled step; the none step's label reads as *inherits* when
    `localModelFrom` is set, since choosing none there does not clear the
    inheritance, it only stores none on this issue's own row. */
export function LocalModelBand({
  current,
  localModelFrom,
  inheritWord,
  landing,
  onPick,
}: {
  current: LocalModel;
  localModelFrom: string | null;
  inheritWord: string;
  landing: LocalModel | null | undefined;
  onPick: (level: LocalModel) => void;
}) {
  const buttons = useRef(new Map<LocalModel, HTMLButtonElement>());

  useEffect(() => {
    if (landing == null) return;
    buttons.current.get(landing)?.focus();
    // Once, on mount: a focus request is for the moment the band opens, not
    // for every re-render while it stays open.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div className="hatch-localmodel-steps" role="group" aria-label="Set local model">
      {LEVELS.map((level) => {
        const here = level.name === current;
        const label =
          level.name === 'none' && localModelFrom ? `None (inherits ${inheritWord} from ${localModelFrom})` : level.word;
        return (
          <button
            key={level.name}
            ref={(el) => {
              if (el) buttons.current.set(level.name, el);
              else buttons.current.delete(level.name);
            }}
            type="button"
            className={`hatch-localmodel-step${level.class ? ` ${level.class}` : ''}${here ? ' here' : ''}`}
            title={level.title}
            aria-pressed={here}
            disabled={here}
            onClick={() => onPick(level.name)}
          >
            {level.glyph && (
              <span aria-hidden="true">
                {level.glyph}
                {' '}
              </span>
            )}
            {label}
          </button>
        );
      })}
    </div>
  );
}

/**
 * The local-model pill, and the band it opens onto: how far this issue leans
 * on a local model rather than the cloud one, drawn the same shape as
 * `PreApprovalControl` for the same reason - one press names exactly the level
 * it sets.
 *
 * A pill that at rest wears the effective level (`localModel`) and, pressed,
 * opens a band holding one step per `LEVELS` entry, the issue's own level
 * (`localModelOwn`) marked. `open` and `landing` are its only state, so the
 * caller mounting this fresh per issue (`key={issue.key}`) always starts
 * folded.
 *
 * A press folds the band, returns focus to the pill, and only then calls
 * `onChange`, so the repaint the caller triggers never fights a band still
 * reading a stale level. Escape is handled on the pill and the band, stopping
 * propagation before it reaches Modal's document listener.
 *
 * Presentational and fetching nothing: a refusal lands in the page's own
 * error line in the server's own words, and this never has an opinion about
 * whether a press worked.
 */
export function LocalModelControl({
  issueKey,
  localModel,
  localModelOwn,
  localModelFrom,
  directory,
  busy = false,
  size = 'md',
  onChange,
}: {
  issueKey: string;
  /** The level in effect - what the pill wears. */
  localModel: LocalModel;
  /** This issue's own level - the step the band marks as current. */
  localModelOwn: LocalModel;
  /** The ancestor the effective level came from, or null when it is this
      issue's own, or none. */
  localModelFrom: string | null;
  /** Everybody who could own an issue, and who the caller is. Null while it is
      still loading. */
  directory: AssigneeDirectory | null;
  /** A press is already out: the pill stays where it is and stops answering. */
  busy?: boolean;
  /** `lg` is the issue page's status bar, drawn the size of its status pill. */
  size?: 'md' | 'lg';
  onChange: (localModel: LocalModel) => void;
}) {
  const [open, setOpen] = useState(false);
  const [landing, setLanding] = useState<LocalModel | null | undefined>(undefined);
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
    setLanding(event.detail === 0 ? priorityLandingFocus(LEVELS, localModelOwn) : undefined);
    setOpen(true);
  };

  const handleChange = (level: LocalModel) => {
    fold();
    onChange(level);
  };

  const effective = levelOf(localModel);

  /* `kind === 'person'`: the write is closed to an API key, so a key holding
     this page would be offered a press that could only be refused. It still
     gets the state, drawn as a word rather than as a dead button. */
  if (directory?.me?.kind !== 'person') {
    return (
      <span className={`hatch-localmodel-said${effective.class ? ` ${effective.class}` : ''}`}>
        {effective.word}
        {localModelFrom ? ` · inherited from ${localModelFrom}` : ''}
      </span>
    );
  }

  const label = `Local model: ${effective.word}${localModelFrom ? `, inherited from ${localModelFrom}` : ''}. Set ${issueKey}'s local model`;

  return (
    <>
      <button
        ref={buttonRef}
        type="button"
        className={`hatch-localmodel-pill hatch-localmodel-pill-press${size === 'lg' ? ' hatch-localmodel-pill-lg' : ''}${effective.class ? ` ${effective.class}` : ''}`}
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
        <svg className="hatch-localmodel-pill-caret" viewBox="0 0 12 12" width="12" height="12" aria-hidden="true">
          <path d="M2.5 4.5 6 8l3.5-3.5" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      </button>

      {open && (
        <div
          id={bandId}
          className={`hatch-localmodel-band${effective.class ? ` ${effective.class}` : ''}`}
          onKeyDown={onEscape}
        >
          <span className="hatch-status-bar-label">Set local model</span>
          <LocalModelBand
            current={localModelOwn}
            localModelFrom={localModelFrom}
            inheritWord={effective.word}
            landing={landing}
            onPick={handleChange}
          />
        </div>
      )}
    </>
  );
}
