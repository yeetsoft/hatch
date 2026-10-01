import { useEffect, useId, useRef, useState } from 'react';
import type { KeyboardEvent as ReactKeyboardEvent, MouseEvent as ReactMouseEvent } from 'react';
import { priorityLandingFocus } from '../lib/priority';
import type { AssigneeDirectory } from '../types';

export type Priority = 'normal' | 'expedited' | 'emergency' | 'paused';

interface PriorityLevel {
  name: Priority;
  word: string;
  glyph: string;
  class: string;
  title: string;
}

/** Every level the picker offers, in the order the band draws them. HA-177
    adds economy here, between normal and paused - this is the one place in
    the component that names a level. */
const LEVELS: PriorityLevel[] = [
  {
    name: 'emergency',
    word: 'Emergency',
    glyph: '🚨',
    class: 'emergency',
    title:
      'The highest priority: goes first, above everything expedited, and the dispatcher considers it before anything else.',
  },
  {
    name: 'expedited',
    word: 'Expedited',
    glyph: '↑',
    class: 'expedited',
    title: 'This one goes first: it floats to the top of its column and the dispatcher reaches for it before anything else.',
  },
  {
    name: 'normal',
    word: 'Normal',
    glyph: '',
    class: '',
    title: 'No float: this one waits its turn in the column, in the usual order.',
  },
  // economy: added by HA-177, between normal and paused - not here yet.
  {
    name: 'paused',
    word: 'Paused',
    glyph: '⏸',
    class: 'paused',
    title: 'Paused: a person set this aside; the loop leaves it where it stands until they set it back.',
  },
];

const NORMAL = LEVELS.find((level) => level.name === 'normal')!;
const levelOf = (name: Priority): PriorityLevel => LEVELS.find((level) => level.name === name) ?? NORMAL;

/** The band's one button per level - `StatusSteps` in miniature, so the issue
    page and the peek can never disagree about what is offered. The own level
    (`current`) is the marked, disabled step; the normal step's label reads as
    *Inherit* when `priorityFrom` is set, since choosing normal there does not
    clear the inheritance, it only stores normal on this issue's own row. */
function PriorityBand({
  current,
  priorityFrom,
  inheritWord,
  landing,
  onPick,
}: {
  current: Priority;
  priorityFrom: string | null;
  inheritWord: string;
  landing: Priority | null | undefined;
  onPick: (level: Priority) => void;
}) {
  const buttons = useRef(new Map<Priority, HTMLButtonElement>());

  useEffect(() => {
    if (landing == null) return;
    buttons.current.get(landing)?.focus();
    // Once, on mount: a focus request is for the moment the band opens, not
    // for every re-render while it stays open.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div className="hatch-priority-steps" role="group" aria-label="Set priority">
      {LEVELS.map((level) => {
        const here = level.name === current;
        const label =
          level.name === 'normal' && priorityFrom ? `Normal (inherits ${inheritWord} from ${priorityFrom})` : level.word;
        return (
          <button
            key={level.name}
            ref={(el) => {
              if (el) buttons.current.set(level.name, el);
              else buttons.current.delete(level.name);
            }}
            type="button"
            className={`hatch-priority-step${level.class ? ` ${level.class}` : ''}${here ? ' here' : ''}`}
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
 * The priority pill, and the band it opens onto: one press names exactly the
 * level it sets, replacing `ExpediteControl`'s cycle - see that file's history
 * for why a control whose state and whose action are different words gets
 * pressed twice.
 *
 * Shaped after `StatusPicker`: a pill that at rest wears the effective level
 * (`priority`) and, pressed, opens a band holding one step per `LEVELS` entry,
 * the issue's own level (`priorityOwn`) marked. `open` and `landing` are its
 * only state, so the caller mounting this fresh per issue (`key={card.key}`
 * on the peek) always starts folded.
 *
 * A press folds the band, returns focus to the pill, and only then calls
 * `onChange` - so the repaint the caller triggers never fights a band still
 * reading a stale level. Escape is handled on the pill and the band, stopping
 * propagation before it reaches Modal's document listener, the same as
 * `StatusPicker`.
 *
 * Presentational and fetching nothing: the page loads the directory once
 * beside its other reads and hands it down, so a refusal lands in the page's
 * own error line in the server's own words and this never has an opinion
 * about whether a press worked.
 */
export function PriorityControl({
  issueKey,
  priority,
  priorityOwn,
  priorityFrom,
  directory,
  busy = false,
  onChange,
}: {
  issueKey: string;
  /** The level in effect - what the pill wears. */
  priority: Priority;
  /** This issue's own level - the step the band marks as current. */
  priorityOwn: Priority;
  /** The ancestor the effective level came from, or null when it is this
      issue's own, or normal. */
  priorityFrom: string | null;
  /** Everybody who could own an issue, and who the caller is. Null while it is
      still loading. */
  directory: AssigneeDirectory | null;
  /** A press is already out: the pill stays where it is and stops answering. */
  busy?: boolean;
  onChange: (priority: Priority) => void;
}) {
  const [open, setOpen] = useState(false);
  const [landing, setLanding] = useState<Priority | null | undefined>(undefined);
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
    setLanding(event.detail === 0 ? priorityLandingFocus(LEVELS, priorityOwn) : undefined);
    setOpen(true);
  };

  const handleChange = (level: Priority) => {
    fold();
    onChange(level);
  };

  const effective = levelOf(priority);

  /* `kind === 'person'` as well as "somebody is here": the write is closed to
     an API key, so a key holding this page would be offered a press that
     could only be refused. It still gets the state, drawn as a word rather
     than as a dead button - an agent reading this page is entitled to know
     why its ticket was reached first, and a control it cannot use is not how
     to tell it. */
  if (directory?.me?.kind !== 'person') {
    return (
      <span className={`hatch-priority-said${effective.class ? ` ${effective.class}` : ''}`}>
        {effective.word}
        {priorityFrom ? ` · inherited from ${priorityFrom}` : ''}
      </span>
    );
  }

  const label = `Priority: ${effective.word}${priorityFrom ? `, inherited from ${priorityFrom}` : ''}. Set ${issueKey}'s priority`;

  return (
    <>
      <button
        ref={buttonRef}
        type="button"
        className={`hatch-priority-pill hatch-priority-pill-press${effective.class ? ` ${effective.class}` : ''}`}
        aria-expanded={open}
        aria-controls={bandId}
        aria-busy={busy}
        aria-label={label}
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
        <svg className="hatch-priority-pill-caret" viewBox="0 0 12 12" width="12" height="12" aria-hidden="true">
          <path d="M2.5 4.5 6 8l3.5-3.5" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      </button>

      {open && (
        <div
          id={bandId}
          className={`hatch-priority-band${effective.class ? ` ${effective.class}` : ''}`}
          onKeyDown={onEscape}
        >
          <span className="hatch-status-bar-label">Set priority</span>
          <PriorityBand
            current={priorityOwn}
            priorityFrom={priorityFrom}
            inheritWord={effective.word}
            landing={landing}
            onPick={handleChange}
          />
        </div>
      )}
    </>
  );
}
