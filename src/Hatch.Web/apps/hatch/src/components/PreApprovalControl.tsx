import { useEffect, useId, useRef, useState } from 'react';
import type { KeyboardEvent as ReactKeyboardEvent, MouseEvent as ReactMouseEvent } from 'react';
import { priorityLandingFocus } from '../lib/priority';
import type { AssigneeDirectory } from '../types';

export type PreApproval = 'none' | 'express' | 'preapproved';

interface PreApprovalLevel {
  name: PreApproval;
  word: string;
  glyph: string;
  class: string;
  title: string;
}

/** Why a step is disabled for permission rather than for being current - the
    exact sentence `IssuePreApprovalController.SetAsync` refuses the write
    with, so a press this band offers never meets a 403 the server already
    knew about. */
const PERMISSION_TITLE =
  "Only an owner or approver of the project may set pre-approval - it is the approve press given in advance";

/** Every level the picker offers, in the order the band draws them - this is
    the one place in the component that names a level. */
const LEVELS: PreApprovalLevel[] = [
  {
    name: 'none',
    word: 'None',
    glyph: '',
    class: '',
    title: 'No approval banked: this issue asks for its own press at every gate, the same as any other.',
  },
  {
    name: 'express',
    word: 'Express',
    glyph: '»',
    class: 'express',
    title:
      'Carried past every column marked Express skips, with no session, whenever it holds no open question. Changes no gate but that one.',
  },
  {
    name: 'preapproved',
    word: 'Pre-approved',
    glyph: '✓',
    class: 'preapproved',
    title:
      'The approve press given in advance: carried past every column marked Express skips, and past the approval gate itself, whenever it holds no open question.',
  },
];

const NONE = LEVELS.find((level) => level.name === 'none')!;
const levelOf = (name: PreApproval): PreApprovalLevel => LEVELS.find((level) => level.name === name) ?? NONE;

/** The band's one button per level - `StatusSteps` in miniature, so the issue
    page and the peek can never disagree about what is offered. The own level
    (`current`) is the marked, disabled step; the none step's label reads as
    *inherits* when `preApprovalFrom` is set, since choosing none there does
    not clear the inheritance, it only stores none on this issue's own row.
    `canApprove` locks every step but the current one once the issue's own
    level is already preapproved, and locks the preapproved step outright
    otherwise - lowering out of preapproved is the approver's act too, the
    same as setting it. */
function PreApprovalBand({
  current,
  preApprovalFrom,
  inheritWord,
  landing,
  canApprove,
  onPick,
}: {
  current: PreApproval;
  preApprovalFrom: string | null;
  inheritWord: string;
  landing: PreApproval | null | undefined;
  canApprove: boolean;
  onPick: (level: PreApproval) => void;
}) {
  const buttons = useRef(new Map<PreApproval, HTMLButtonElement>());

  useEffect(() => {
    if (landing == null) return;
    buttons.current.get(landing)?.focus();
    // Once, on mount: a focus request is for the moment the band opens, not
    // for every re-render while it stays open.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div className="hatch-preapproval-steps" role="group" aria-label="Set pre-approval">
      {LEVELS.map((level) => {
        const here = level.name === current;
        const permissionLocked = !here && !canApprove && (level.name === 'preapproved' || current === 'preapproved');
        const disabled = here || permissionLocked;
        const label =
          level.name === 'none' && preApprovalFrom ? `None (inherits ${inheritWord} from ${preApprovalFrom})` : level.word;
        return (
          <button
            key={level.name}
            ref={(el) => {
              if (el) buttons.current.set(level.name, el);
              else buttons.current.delete(level.name);
            }}
            type="button"
            className={`hatch-preapproval-step${level.class ? ` ${level.class}` : ''}${here ? ' here' : ''}`}
            title={permissionLocked ? PERMISSION_TITLE : level.title}
            aria-pressed={here}
            disabled={disabled}
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
 * The pre-approval pill, and the band it opens onto: the approve press given
 * in advance, drawn the same shape as `PriorityControl` for the same reason -
 * one press names exactly the level it sets.
 *
 * Shaped after `PriorityControl`: a pill that at rest wears the effective
 * level (`preApproval`) and, pressed, opens a band holding one step per
 * `LEVELS` entry, the issue's own level (`preApprovalOwn`) marked. `open` and
 * `landing` are its only state, so the caller mounting this fresh per issue
 * (`key={card.key}` on the peek) always starts folded.
 *
 * A press folds the band, returns focus to the pill, and only then calls
 * `onChange` - so the repaint the caller triggers never fights a band still
 * reading a stale level. Escape is handled on the pill and the band, stopping
 * propagation before it reaches Modal's document listener, the same as
 * `PriorityControl`.
 *
 * Presentational and fetching nothing: the page loads the directory and the
 * project once beside its other reads and hands them down, so a refusal
 * lands in the page's own error line in the server's own words and this
 * never has an opinion about whether a press worked. `canApprove` is the
 * project's own flag, already resolved by the caller - this never fetches it.
 */
export function PreApprovalControl({
  issueKey,
  preApproval,
  preApprovalOwn,
  preApprovalFrom,
  directory,
  canApprove,
  busy = false,
  onChange,
}: {
  issueKey: string;
  /** The level in effect - what the pill wears. */
  preApproval: PreApproval;
  /** This issue's own level - the step the band marks as current. */
  preApprovalOwn: PreApproval;
  /** The ancestor the effective level came from, or null when it is this
      issue's own, or none. */
  preApprovalFrom: string | null;
  /** Everybody who could own an issue, and who the caller is. Null while it is
      still loading. */
  directory: AssigneeDirectory | null;
  /** Whether the caller may set or lower preapproved on this issue's project -
      resolved by the caller, the same as `directory` already is. */
  canApprove: boolean;
  /** A press is already out: the pill stays where it is and stops answering. */
  busy?: boolean;
  onChange: (preApproval: PreApproval) => void;
}) {
  const [open, setOpen] = useState(false);
  const [landing, setLanding] = useState<PreApproval | null | undefined>(undefined);
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
    setLanding(event.detail === 0 ? priorityLandingFocus(LEVELS, preApprovalOwn) : undefined);
    setOpen(true);
  };

  const handleChange = (level: PreApproval) => {
    fold();
    onChange(level);
  };

  const effective = levelOf(preApproval);

  /* `kind === 'person'` as well as "somebody is here": the write is closed to
     an API key, so a key holding this page would be offered a press that
     could only be refused. It still gets the state, drawn as a word rather
     than as a dead button - an agent reading this page is entitled to know
     why its ticket was reached first, and a control it cannot use is not how
     to tell it. */
  if (directory?.me?.kind !== 'person') {
    return (
      <span className={`hatch-preapproval-said${effective.class ? ` ${effective.class}` : ''}`}>
        {effective.word}
        {preApprovalFrom ? ` · inherited from ${preApprovalFrom}` : ''}
      </span>
    );
  }

  const label = `Pre-approval: ${effective.word}${preApprovalFrom ? `, inherited from ${preApprovalFrom}` : ''}. Set ${issueKey}'s pre-approval`;

  return (
    <>
      <button
        ref={buttonRef}
        type="button"
        className={`hatch-preapproval-pill hatch-preapproval-pill-press${effective.class ? ` ${effective.class}` : ''}`}
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
        <svg className="hatch-preapproval-pill-caret" viewBox="0 0 12 12" width="12" height="12" aria-hidden="true">
          <path d="M2.5 4.5 6 8l3.5-3.5" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      </button>

      {open && (
        <div
          id={bandId}
          className={`hatch-preapproval-band${effective.class ? ` ${effective.class}` : ''}`}
          onKeyDown={onEscape}
        >
          <span className="hatch-status-bar-label">Set pre-approval</span>
          <PreApprovalBand
            current={preApprovalOwn}
            preApprovalFrom={preApprovalFrom}
            inheritWord={effective.word}
            landing={landing}
            canApprove={canApprove}
            onPick={handleChange}
          />
        </div>
      )}
    </>
  );
}
