import { useState } from 'react';
import { glyphState, levelAfterPress } from '../lib/projectLevel';
import type { LevelGlyph } from '../lib/projectLevel';
import { ExpediteGlyph } from './ExpediteGlyph';
import { PauseGlyph } from './PauseGlyph';
import type { ProjectPriority } from '../types';

/** An expedite or pause button, for an owner alone - holds its own
    hover/focus state, since that is what half-fills the glyph before a
    level actually changes. `name` is the project's `KEY — Name` text. */
export function LevelButton({
  current,
  name,
  glyph,
  busy = false,
  onPress,
}: {
  current: ProjectPriority;
  name: string;
  glyph: LevelGlyph;
  busy?: boolean;
  onPress: () => void;
}) {
  const [active, setActive] = useState(false);
  const state = glyphState(current, glyph, active);
  const next = levelAfterPress(current, glyph);
  const label =
    glyph === 'expedite'
      ? next === 'expedited'
        ? `Expedite ${name}`
        : `Stop expediting ${name}`
      : next === 'paused'
        ? `Pause ${name}`
        : `Resume ${name}`;

  return (
    <button
      type="button"
      className={`hatch-scope-control__level hatch-scope-control__level--${glyph}`}
      aria-pressed={state === 'filled'}
      aria-label={label}
      disabled={busy}
      onMouseEnter={() => setActive(true)}
      onMouseLeave={() => setActive(false)}
      onFocus={() => setActive(true)}
      onBlur={() => setActive(false)}
      onClick={onPress}
    >
      {glyph === 'expedite' ? <ExpediteGlyph state={state} /> : <PauseGlyph state={state} />}
    </button>
  );
}
