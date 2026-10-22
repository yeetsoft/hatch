import { HATCH_MARK_BODY, HATCH_MARK_CAP, HATCH_MARK_VIEWBOX } from '../brand/hatchMark';

export interface HatchMarkProps {
  /** Width and height. 28 is the bar's. */
  size?: number;
  className?: string;
}

/**
 * The Hatch mark - the egg, hatching - drawn in currentColor so it takes the
 * ink of wherever it sits: --on-chrome on the bar, --ink on a page. It is
 * decoration, never a control on its own: aria-hidden, with the text beside
 * it naming whatever it is attached to. The geometry is ../brand/hatchMark.ts's;
 * this draws it and nothing else.
 */
export function HatchMark({ size = 28, className }: HatchMarkProps) {
  return (
    <svg
      className={className}
      width={size}
      height={size}
      viewBox={HATCH_MARK_VIEWBOX}
      fill="currentColor"
      aria-hidden="true"
      focusable="false"
    >
      <path d={HATCH_MARK_CAP} />
      <path d={HATCH_MARK_BODY} />
    </svg>
  );
}
