/* The two glyphs on the palette line - a die and a copy sign - as data.
   <ThemeSwitch> draws them with React elements and standalone/topbar.ts with
   DOM calls; the shapes are written once here so the two cannot drift. A 24
   viewBox, stroked in currentColor, decoration only (the buttons carry the
   names). */

export interface IconShape {
  tag: 'rect' | 'circle';
  attrs: Record<string, string>;
}

export const PICKER_ICON_VIEWBOX = '0 0 24 24';

const pip = (cx: string, cy: string): IconShape => ({
  tag: 'circle',
  attrs: { cx, cy, r: '1.3', fill: 'currentColor', stroke: 'none' },
});

export const DIE_SHAPES: IconShape[] = [
  { tag: 'rect', attrs: { x: '4', y: '4', width: '16', height: '16', rx: '3.5' } },
  pip('8.8', '8.8'),
  pip('15.2', '8.8'),
  pip('12', '12'),
  pip('8.8', '15.2'),
  pip('15.2', '15.2'),
];

export const COPY_SHAPES: IconShape[] = [
  { tag: 'rect', attrs: { x: '8.5', y: '8.5', width: '11', height: '11', rx: '2.5' } },
  { tag: 'rect', attrs: { x: '4.5', y: '4.5', width: '11', height: '11', rx: '2.5' } },
];
