import type { GlyphState } from '../lib/projectLevel';

/**
 * The pause mark a scope row's owner presses to pause a project: two bars,
 * each outlined at rest and solid at the glyph's own level (`filled`). The
 * two bars are already a left and a right half, so `half` fills only the
 * left one rather than needing ExpediteGlyph's clipPath.
 */
export function PauseGlyph({ state }: { state: GlyphState }) {
  const leftFilled = state === 'filled' || state === 'half';
  const rightFilled = state === 'filled';
  return (
    <svg
      viewBox="0 0 24 24"
      width={16}
      height={16}
      stroke="currentColor"
      strokeWidth={2}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      <rect x={5} y={4} width={5} height={16} rx={1} fill={leftFilled ? 'currentColor' : 'none'} />
      <rect x={14} y={4} width={5} height={16} rx={1} fill={rightFilled ? 'currentColor' : 'none'} />
    </svg>
  );
}
