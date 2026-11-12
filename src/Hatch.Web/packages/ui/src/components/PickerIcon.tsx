import { PICKER_ICON_VIEWBOX, type IconShape } from './themePickerIcons';

/** One of the palette line's glyphs (see themePickerIcons.ts), stroked in the colour of the button it sits in.
    Decoration only: the button carries the name. */
export function PickerIcon({ shapes }: { shapes: IconShape[] }) {
  return (
    <svg
      viewBox={PICKER_ICON_VIEWBOX}
      width="18"
      height="18"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      aria-hidden="true"
      focusable="false"
    >
      {shapes.map((shape, at) =>
        shape.tag === 'rect' ? <rect key={at} {...shape.attrs} /> : <circle key={at} {...shape.attrs} />,
      )}
    </svg>
  );
}
