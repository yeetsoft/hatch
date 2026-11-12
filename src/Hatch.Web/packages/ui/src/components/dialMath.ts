/* The hue dial's arithmetic, apart from the component so it is testable without a DOM.
   The angle convention is the gallery sketch's: 0 at 3 o'clock, clockwise, as SVG's own y axis runs. */

/** Degrees in [0, 360) for a point's offset from the dial's centre. */
export function angleFromPoint(dx: number, dy: number): number {
  const degrees = (Math.atan2(dy, dx) * 180) / Math.PI;
  return (degrees + 360) % 360;
}

/**
 * What a key does to the dial's value, or null for a key it ignores. The arrows move one degree, Shift fifteen;
 * Home and End go to the ends of the range. The result is clamped, not wrapped - the arrows stop at 0 and 360
 * as `aria-valuemin` and `aria-valuemax` say, where a drag, which is an angle, wraps through the seam.
 */
export function keyValue(key: string, shift: boolean, value: number, min: number, max: number): number | null {
  const by = shift ? 15 : 1;
  switch (key) {
    case 'ArrowRight':
    case 'ArrowUp':
      return Math.min(value + by, max);
    case 'ArrowLeft':
    case 'ArrowDown':
      return Math.max(value - by, min);
    case 'Home':
      return min;
    case 'End':
      return max;
    default:
      return null;
  }
}

/** A point on the dial's rim, in the 100-unit viewBox. */
export function rimPoint(degrees: number, radius: number, centre = 50): { x: number; y: number } {
  const rad = (degrees * Math.PI) / 180;
  return { x: centre + radius * Math.cos(rad), y: centre + radius * Math.sin(rad) };
}
