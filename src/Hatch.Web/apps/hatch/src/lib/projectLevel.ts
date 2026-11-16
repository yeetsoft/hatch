import type { ProjectPriority } from '../types';

/** Which of the scope row's two glyphs was pressed. */
export type LevelGlyph = 'expedite' | 'pause';

/** How a glyph draws: outlined at rest, filled at its own level, half-filled
    while hovered or focused and not yet there. */
export type GlyphState = 'outline' | 'filled' | 'half';

/** The level a press leaves a project at. The arrow toggles expedited; pause
    toggles paused; each is a no-op on the other's level, so pressing the
    arrow on a paused project expedites it, and pausing an expedited one
    pauses it - neither reads back to normal first. */
export function levelAfterPress(current: ProjectPriority, glyph: LevelGlyph): ProjectPriority {
  if (glyph === 'expedite') return current === 'expedited' ? 'normal' : 'expedited';
  return current === 'paused' ? 'normal' : 'paused';
}

/** How a glyph should draw: filled at its own level, half while hovered or
    focused and not already there, outline otherwise. */
export function glyphState(current: ProjectPriority, glyph: LevelGlyph, active: boolean): GlyphState {
  const own: ProjectPriority = glyph === 'expedite' ? 'expedited' : 'paused';
  if (current === own) return 'filled';
  return active ? 'half' : 'outline';
}
