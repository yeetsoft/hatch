/** The glyph's own units. A 24-unit box, matching the battery beside it. */
const SIZE = 24;

/**
 * A robot head with an antenna: the nav's attention control, and a claimed
 * card's mark, drawn from the one shape.
 *
 * The whole thing is muted at rest with the antenna's lamp unlit. When
 * something is waiting - or, on a card, when a runner holds the claim - the
 * lamp and the eyes light. Every part strokes `currentColor`, so a call
 * site's own `color` is what picks the nav's ink, blue or amber; `className`
 * is what picks its size, and is where a call site's own motion - the nav's
 * blink - gets scoped in CSS (App.css).
 */
export function Robot({ lit, className }: { lit: boolean; className?: string }) {
  return (
    <svg className={className} viewBox={`0 0 ${SIZE} ${SIZE}`} aria-hidden="true" focusable="false">
      {/* The antenna: a stalk up out of the head, and the lamp on top of it. */}
      <line className="hatch-robot-antenna" x1="12" y1="6" x2="12" y2="3.5" />
      <circle className={`hatch-robot-lamp${lit ? ' lit' : ''}`} cx="12" cy="2.5" r="1.75" />

      <rect className="hatch-robot-head" x="4" y="6" width="16" height="13" rx="4" />

      <circle className={`hatch-robot-eye${lit ? ' lit' : ''}`} cx="9" cy="12" r="1.5" />
      <circle className={`hatch-robot-eye${lit ? ' lit' : ''}`} cx="15" cy="12" r="1.5" />
    </svg>
  );
}
