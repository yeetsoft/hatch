import type { Project, ProjectPriority } from '../types';

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

/** The projects at each non-normal level, each in the list's own order - what
    the scope control's cluster draws beside its trigger. */
export function leveled<T extends { priority: ProjectPriority }>(projects: T[]): { expedited: T[]; paused: T[] } {
  return {
    expedited: projects.filter((project) => project.priority === 'expedited'),
    paused: projects.filter((project) => project.priority === 'paused'),
  };
}

/**
 * Moves a project to a level: drawn at once through `onApply`, written with
 * `put`, then redrawn from the server's answer. A refused write puts the
 * original back and throws again, so the caller can say why.
 */
export async function setLevel(
  project: Project,
  level: ProjectPriority,
  { put, onApply }: { put: (request: { priority: ProjectPriority }) => Promise<Project>; onApply: (project: Project) => void },
): Promise<void> {
  onApply({ ...project, priority: level });
  try {
    onApply(await put({ priority: level }));
  } catch (err) {
    onApply(project);
    throw err;
  }
}
