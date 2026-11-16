import { useId } from 'react';
import type { GlyphState } from '../lib/projectLevel';

const SHAPE = '12,3 19,11 15,11 15,20 9,20 9,11 5,11';

/**
 * The up arrow a scope row's owner presses to expedite a project. Outlined
 * at rest, solid at its own level (`filled`), and half-filled on the left
 * while hovered or focused (`half`) - a second copy of the same shape, clipped
 * to the left half, laid over the outline. `currentColor`, so App.css's
 * `--level-up` on the button is what colours it.
 */
export function ExpediteGlyph({ state }: { state: GlyphState }) {
  const clipId = useId();
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
      <polygon points={SHAPE} fill={state === 'filled' ? 'currentColor' : 'none'} />
      {state === 'half' && (
        <>
          <clipPath id={clipId}>
            <rect x={0} y={0} width={12} height={24} />
          </clipPath>
          <polygon points={SHAPE} fill="currentColor" stroke="none" clipPath={`url(#${clipId})`} />
        </>
      )}
    </svg>
  );
}
