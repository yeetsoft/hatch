/* Eight identical teeth around the hub, one rotation apart, rather than a
   single hand-drawn cog outline: rotate(45n) around the icon's own center is
   arithmetic a reviewer can check, where a free-hand path is not. */
const TOOTH_ANGLES = [0, 45, 90, 135, 180, 225, 270, 315];

/**
 * The gear menu's own artwork, alone in a file on the same principle
 * `AppsMark.tsx` was: replacing it is one file, with no hunting through a
 * component that also carries a hit target, a hover state and a focus ring.
 *
 * `currentColor` throughout, so the mark takes the ink of whatever surface it
 * is placed on and needs no answer of its own for dark.
 */
export function GearIcon({ className }: { className?: string }) {
  return (
    <svg
      className={className}
      viewBox="0 0 24 24"
      width={20}
      height={20}
      fill="currentColor"
      /* Decoration: the trigger around it carries the accessible name, and a
         mark that also announced itself would say the same thing twice.
         `focusable` is for IE-era Edge, which put SVGs in the tab order. */
      aria-hidden="true"
      focusable="false"
    >
      {/* The hub: an outer circle (r=4) with a concentric hole (r=2.2) cut out
          by the even-odd fill rule, each drawn as two half-arcs. */}
      <path
        fillRule="evenodd"
        d="M16,12 A4,4 0 1 1 8,12 A4,4 0 1 1 16,12 Z M14.2,12 A2.2,2.2 0 1 1 9.8,12 A2.2,2.2 0 1 1 14.2,12 Z"
      />
      {TOOTH_ANGLES.map((angle) => (
        <rect
          key={angle}
          x={10.5}
          y={3}
          width={3}
          height={5}
          rx={0.8}
          transform={angle ? `rotate(${angle} 12 12)` : undefined}
        />
      ))}
    </svg>
  );
}
