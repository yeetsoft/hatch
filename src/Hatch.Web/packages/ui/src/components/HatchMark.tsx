import { useId } from 'react';
import { HATCH_MARK_BODY, HATCH_MARK_BOUNDS, HATCH_MARK_CAP, HATCH_MARK_VIEWBOX } from '../brand/hatchMark';

export interface HatchMarkProps {
  /** Width and height. 28 is the bar's. */
  size?: number;
  className?: string;
  /** Paints the egg from --primary to --chrome-end instead of currentColor -
      the one coloured egg licensed inside an app document, the sign-in
      page's mark. Off by default, so the bar's mark is unchanged. */
  gradient?: boolean;
}

/**
 * The Hatch mark - the egg, hatching - drawn in currentColor so it takes the
 * ink of wherever it sits: --on-chrome on the bar, --ink on a page. It is
 * decoration, never a control on its own: aria-hidden, with the text beside
 * it naming whatever it is attached to. The geometry is ../brand/hatchMark.ts's;
 * this draws it and nothing else.
 *
 * `gradient` reads --primary/--chrome-end directly, where hatchMarkSvg's own
 * 'gradient' fill is a literal pair - that file generates assets fetched
 * outside any document, where no token is in scope.
 */
export function HatchMark({ size = 28, className, gradient = false }: HatchMarkProps) {
  const rawId = useId();
  const gradientId = `hatch-mark-${rawId.replace(/:/g, '')}`;
  const { x1, y1, x2, y2 } = HATCH_MARK_BOUNDS;

  return (
    <svg
      className={className}
      width={size}
      height={size}
      viewBox={HATCH_MARK_VIEWBOX}
      fill={gradient ? `url(#${gradientId})` : 'currentColor'}
      aria-hidden="true"
      focusable="false"
    >
      {gradient && (
        <defs>
          <linearGradient id={gradientId} gradientUnits="userSpaceOnUse" x1={x1} y1={y1} x2={x2} y2={y2}>
            <stop offset="0" style={{ stopColor: 'var(--primary)' }} />
            <stop offset="1" style={{ stopColor: 'var(--chrome-end)' }} />
          </linearGradient>
        </defs>
      )}
      <path d={HATCH_MARK_CAP} />
      <path d={HATCH_MARK_BODY} />
    </svg>
  );
}
