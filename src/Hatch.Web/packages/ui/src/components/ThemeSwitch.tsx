import { useEffect, useId, useLayoutEffect, useRef, useState, type KeyboardEvent } from 'react';
import { useTheme } from '../theme/useTheme';
import type { ThemeChoice } from '../theme/themeContext';
import { checkSeed, rollSeed } from '../theme/randomPalette';
import { COPY_SHAPES, DIE_SHAPES, PICKER_ICON_VIEWBOX, type IconShape } from './themePickerIcons';
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

/** How long "Seed copied" stays up. */
const COPIED_MS = 2000;

function Icon({ shapes }: { shapes: IconShape[] }) {
  return (
    <svg
      viewBox={PICKER_ICON_VIEWBOX}
      width="18"
      height="18"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      aria-hidden="true"
      focusable="false"
    >
      {shapes.map((shape, at) =>
        shape.tag === 'rect' ? <rect key={at} {...shape.attrs} /> : <circle key={at} {...shape.attrs} />,
      )}
    </svg>
  );
}

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
  const [copied, setCopied] = useState('');
  const copiedTimer = useRef<ReturnType<typeof setTimeout>>(undefined);
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

  useEffect(() => () => clearTimeout(copiedTimer.current), []);

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

  const announce = (message: string) => {
    setCopied(message);
    clearTimeout(copiedTimer.current);
    copiedTimer.current = setTimeout(() => setCopied(''), COPIED_MS);
  };

  const copy = () => {
    /* Plain http has no clipboard, and a denied permission rejects. */
    const failed = () => announce('Could not copy - select the seed and copy it by hand');
    if (!navigator.clipboard) return failed();
    navigator.clipboard.writeText(shown).then(() => announce('Seed copied'), failed);
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
          <Icon shapes={DIE_SHAPES} />
        </button>
        {/* The configurator's Tune button belongs here, after the dice. */}
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
          onClick={copy}
        >
          <Icon shapes={COPY_SHAPES} />
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
    </div>
  );
}
