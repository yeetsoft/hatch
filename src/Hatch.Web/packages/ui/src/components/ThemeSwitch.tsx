import { useEffect, useId, useLayoutEffect, useRef, useState, type KeyboardEvent } from 'react';
import { useTheme } from '../theme/useTheme';
import type { ThemeChoice } from '../theme/themeContext';
import { checkSeed, rollSeed } from '../theme/randomPalette';
import { shareLink } from '../theme/sharedSeed';
import { COPY_SHAPES, DIE_SHAPES, LINK_SHAPES } from './themePickerIcons';
import { PickerIcon } from './PickerIcon';
import { PaletteConfigurator } from './PaletteConfigurator';
import { useCopyAnnounce } from './useCopyAnnounce';
import './ThemeSwitch.css';

/** The ground the switch is sitting on. `surface` is a page or a card - the
    only ground left since the bar itself now carries the gear rather than the
    switch directly; the prop stays because call sites still pass it
    explicitly. */
export type ThemeSwitchTone = 'surface';

const CHOICES: { value: ThemeChoice; label: string }[] = [
  { value: 'auto', label: 'Auto' },
  { value: 'light', label: 'Light' },
  { value: 'dark', label: 'Dark' },
];

/**
 * The control for the mechanism Phase 1 shipped: `auto` defers to the OS,
 * `light`/`dark` override it and persist. Beneath it, the palette line: Random
 * puts the seeded palette on and off, the dice rolls a new seed, and the seed
 * is text that can be copied and pasted back.
 *
 * Built on real radio inputs rather than buttons with `role="radio"`, because
 * the browser then supplies the whole keyboard contract for free - one tab
 * stop for the group, arrow keys to move within it, and the grouping announced
 * to a screen reader off the <fieldset>/<legend> rather than off an ARIA
 * attribute someone has to remember to keep in sync.
 *
 * `name` is derived from useId rather than a constant so two switches on one
 * page (the gallery shows components in several contexts) stay independent
 * groups instead of silently stealing each other's checked state.
 *
 * Random is on exactly when a palette is applied; turning it off keeps the
 * last seed (the provider holds it) so turning it back on returns that theme.
 */
export function ThemeSwitch({
  tone = 'surface',
  className,
}: {
  tone?: ThemeSwitchTone;
  className?: string;
}) {
  const { choice, setChoice, palette, lastSeed, setPalette } = useTheme();
  const name = useId();
  const messageId = useId();
  const classes = ['hatch-theme-switch', `hatch-theme-switch--${tone}`];
  if (className) classes.push(className);

  const shown = palette ?? lastSeed ?? '';
  const [draft, setDraft] = useState(shown);
  const [refusal, setRefusal] = useState<string | null>(null);
  const { message: copied, copy } = useCopyAnnounce();
  const [tuning, setTuning] = useState(false);
  const tuneRef = useRef<HTMLButtonElement>(null);
  const wasTuning = useRef(false);
  const random = palette !== null;

  /* A roll, a Random toggle or a paste elsewhere moves the seed under the
     field; what was typed gives way to it. Layout, not passive: a roll and the
     read of the field it lands in both sit inside one user gesture (a dice
     click, a driver's read right after), and a passive effect's one extra
     task-loop turn is enough to lose that race. */
  useLayoutEffect(() => {
    setDraft(shown);
    setRefusal(null);
  }, [shown]);

  /* The configurator is portalled out of the gear's panel (which translates and clips, and would hold a fixed
     overlay inside itself), and focus moving into it closes the gear, so by the time the dialog hands focus back
     Tune is in a `visibility: hidden` panel and cannot take it. Where it could not, the gear's own trigger is the
     nearest place the operator was. Runs after the dialog's own cleanup, which is where it tried. */
  useEffect(() => {
    if (wasTuning.current && !tuning) {
      const tune = tuneRef.current;
      if (tune && document.activeElement !== tune) {
        tune.closest<HTMLElement>('.hatch-menu')?.querySelector<HTMLElement>('.hatch-menu__trigger')?.focus();
      }
    }
    wasTuning.current = tuning;
  }, [tuning]);

  const commit = () => {
    const text = draft.trim();
    if (text === '' || text === shown) return;
    const checked = checkSeed(text);
    if (checked.ok) setPalette(checked.seed);
    else setRefusal(checked.message);
  };

  const onSeedKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter') {
      event.preventDefault();
      commit();
    } else if (event.key === 'Escape' && (draft !== shown || refusal !== null)) {
      /* Revert, and keep the Escape: the gear would close on it otherwise. */
      event.stopPropagation();
      setDraft(shown);
      setRefusal(null);
    }
  };

  return (
    <div className="hatch-theme-picker">
      <fieldset className={classes.join(' ')}>
        <legend className="hatch-theme-switch__legend">Theme</legend>
        {CHOICES.map((option) => (
          <label key={option.value} className="hatch-theme-switch__option">
            <input
              type="radio"
              name={name}
              value={option.value}
              checked={choice === option.value}
              onChange={() => setChoice(option.value)}
            />
            <span>{option.label}</span>
          </label>
        ))}
      </fieldset>
      <div className="hatch-theme-picker__line">
        <label className="hatch-theme-picker__random">
          <input
            type="checkbox"
            role="switch"
            checked={random}
            onChange={() => setPalette(random ? null : (lastSeed ?? rollSeed()))}
          />
          <span className="hatch-theme-picker__track" aria-hidden="true" />
          <span>Random</span>
        </label>
        <button
          type="button"
          className="hatch-theme-picker__button"
          aria-label="Roll a new theme"
          title="Roll a new theme"
          disabled={!random}
          onClick={() => setPalette(rollSeed())}
        >
          <PickerIcon shapes={DIE_SHAPES} />
        </button>
        {/* Named by its text, and enabled with Random on or off: tuning from the house palette is how a first seed
            gets made. */}
        <button
          ref={tuneRef}
          type="button"
          className="hatch-theme-picker__button hatch-theme-picker__button--text"
          onClick={() => setTuning(true)}
        >
          Tune
        </button>
        <input
          type="text"
          className="hatch-theme-picker__seed"
          aria-label="Theme seed"
          aria-invalid={refusal !== null || undefined}
          aria-describedby={refusal !== null ? messageId : undefined}
          spellCheck={false}
          autoComplete="off"
          autoCapitalize="off"
          value={draft}
          onChange={(event) => {
            setDraft(event.target.value);
            setRefusal(null);
          }}
          onKeyDown={onSeedKeyDown}
          onBlur={commit}
        />
        <button
          type="button"
          className="hatch-theme-picker__button"
          aria-label="Copy seed"
          title="Copy seed"
          disabled={shown === ''}
          onClick={() => copy(shown, 'Seed copied', 'Could not copy - select the seed and copy it by hand')}
        >
          <PickerIcon shapes={COPY_SHAPES} />
        </button>
        <button
          type="button"
          className="hatch-theme-picker__button"
          aria-label="Copy link"
          title="Copy link"
          disabled={shown === ''}
          onClick={() => copy(shareLink(shown), 'Link copied', 'Could not copy the link')}
        >
          <PickerIcon shapes={LINK_SHAPES} />
        </button>
      </div>
      {refusal !== null ? (
        <p id={messageId} className="hatch-theme-picker__message" role="alert">
          {refusal}
        </p>
      ) : null}
      <span className="hatch-theme-picker__message hatch-theme-picker__status" role="status">
        {copied}
      </span>
      <PaletteConfigurator open={tuning} onClose={() => setTuning(false)} />
    </div>
  );
}
