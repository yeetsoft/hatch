/* The Hatch mark.
   ---------------------------------------------------------------------------
   An egg, one crack across its upper third, the cap lifted straight up: the
   plan hatching. Two closed paths in a 100x100 box, so any renderer draws it
   with no clip and no mask - a browser, a favicon cache, the rasteriser in
   apps/hatch/scripts/generate-icons.mjs.

   The egg is 67 wide and 84 tall: apex at y=12, widest at y=61, bottom at
   y=96, two half-ellipses of different height sharing the widest line. The
   crack is a four-tooth zigzag through y=39..51; the cap is the egg above it,
   drawn 7 units higher than where it broke off, so the gap shows whatever
   ground the mark is on - the bar's gradient through a white egg, the page
   through a coloured one. The cap's lift is baked into its coordinates.

   This file is the geometry's one source. Everything else is generated from
   it by `npm run icons` in apps/hatch: hatch-logo.svg at the repository root,
   the bundler copy in ../assets, the hatch and auth favicons, and the PNG
   icon set. brandAssets.test.ts in apps/hatch refuses a drift between them.
   <HatchMark> (../components/HatchMark.tsx) and the standalone bar draw the
   same two paths in currentColor. */

export const HATCH_MARK_VIEWBOX = '0 0 100 100';

/** The cap, already lifted. */
export const HATCH_MARK_CAP =
  'M19.62 34.63C25.24 17.57 37.62 5 50 5C62.8 5 75.61 18.44 80.93 36.38L71 34L50 44L29 32Z';

/** The body: the crack along its top, the rest of the egg around. */
export const HATCH_MARK_BODY =
  'M19.62 41.63L29 39L50 51L71 41L80.93 43.38C82.57 48.91 83.5 54.87 83.5 61C83.5 80.5 68.53 96 50 96C31.47 96 16.5 80.5 16.5 61C16.5 54.22 17.64 47.64 19.62 41.63Z';

/** The egg's own colour: --primary to --chrome-end from tokens.css, run from
    the egg's top-left to its bottom-right at the bar's own 135deg. The blue
    end is the accent's rather than the bar's, a step brighter, so the egg
    pops on a white page where the bar's own ground would sit a little heavy.
    Literals rather than custom properties because an SVG file is fetched
    outside any document, where no token is in scope; tokens.css is what
    these are kept in step with. */
export const HATCH_MARK_GRADIENT: readonly [string, string] = ['#0066cc', '#4b2fc4'];

/** The icon tile's ground: --chrome to --chrome-end, the bar's exact
    gradient, so the home-screen icon and the bar above the board are one
    colour. The white egg is knocked out of it. */
export const HATCH_ICON_GRADIENT: readonly [string, string] = ['#0052a3', '#4b2fc4'];

/** The egg's own box (the body's extent; the lifted cap rises to y=5). A
    gradient is laid over this box in user space so it spans the whole egg -
    one per piece would restart the colour at the crack. */
export const HATCH_MARK_BOUNDS = { x1: 16.5, y1: 12, x2: 83.5, y2: 96 } as const;

const GRADIENT_ID = 'hatch-mark-gradient';

function gradientDef(): string {
  const { x1, y1, x2, y2 } = HATCH_MARK_BOUNDS;
  const [from, to] = HATCH_MARK_GRADIENT;
  return (
    `<defs><linearGradient id="${GRADIENT_ID}" gradientUnits="userSpaceOnUse" x1="${x1}" y1="${y1}" x2="${x2}" y2="${y2}">` +
    `<stop offset="0" stop-color="${from}"/><stop offset="1" stop-color="${to}"/></linearGradient></defs>`
  );
}

export interface HatchMarkSvgOptions {
  /** Width and height, in CSS pixels. Omitted, the file scales to its box. */
  size?: number;
  /** `gradient` paints the egg in HATCH_MARK_GRADIENT; any other string is
      an SVG fill. `currentColor` by default. */
  fill?: 'gradient' | string;
  /** A leading comment, for a generated file that should say where it came
      from. */
  comment?: string;
}

/**
 * The mark as an SVG document. The favicons, the root logo and the icon
 * rasteriser all take this. Not what <HatchMark> renders - that draws the two
 * paths itself - but the same two paths.
 */
export function hatchMarkSvg({ size, fill = 'currentColor', comment }: HatchMarkSvgOptions = {}): string {
  const paint = fill === 'gradient' ? `url(#${GRADIENT_ID})` : fill;
  const defs = fill === 'gradient' ? gradientDef() : '';
  const dims = size === undefined ? '' : ` width="${size}" height="${size}"`;
  const head = comment === undefined ? '' : `<!-- ${comment} -->\n`;
  return (
    `${head}<svg xmlns="http://www.w3.org/2000/svg" viewBox="${HATCH_MARK_VIEWBOX}"${dims}>` +
    `${defs}<path fill="${paint}" d="${HATCH_MARK_CAP}"/><path fill="${paint}" d="${HATCH_MARK_BODY}"/></svg>\n`
  );
}

/** The superellipse an iOS icon is cut to, as a closed path in the 100 box:
    |x/50|^n + |y/50|^n = 1 with n=4.6, sampled finely enough that the curve
    is smooth at 512px. */
function squirclePath(): string {
  const n = 4.6;
  const steps = 96;
  const points: string[] = [];
  for (let i = 0; i < steps; i++) {
    const t = (i / steps) * Math.PI * 2;
    const c = Math.cos(t);
    const s = Math.sin(t);
    const x = 50 + 50 * Math.sign(c) * Math.abs(c) ** (2 / n);
    const y = 50 + 50 * Math.sign(s) * Math.abs(s) ** (2 / n);
    points.push(`${x.toFixed(2)} ${y.toFixed(2)}`);
  }
  return `M${points.join('L')}Z`;
}

/** The egg's share of the icon tile. At 0.72 the lifted cap's apex sits 33
    units from the tile's centre, inside the 40 that the maskable icon spec
    guarantees visible. */
const ICON_EGG_SCALE = 0.72;

export interface HatchIconSvgOptions {
  /** The tile's width and height in pixels. */
  size: number;
  /** `squircle` for an icon shown as it is, with transparent corners; `square`
      for one the platform masks itself - the maskable PWA icon and the Apple
      touch icon - where the gradient runs to every edge. */
  shape: 'squircle' | 'square';
}

/**
 * The app icon: the mark knocked out of a tile in the bar's gradient, at 72%
 * of the tile and centred on the whole silhouette, lifted cap included. The
 * one place the bare mark takes a tile, because an icon has to be a square.
 */
export function hatchIconSvg({ size, shape }: HatchIconSvgOptions): string {
  const [from, to] = HATCH_ICON_GRADIENT;
  const tileId = 'hatch-icon-tile';
  const tile =
    shape === 'squircle'
      ? `<path fill="url(#${tileId})" d="${squirclePath()}"/>`
      : `<rect fill="url(#${tileId})" width="100" height="100"/>`;
  /* The silhouette runs from the cap's apex at 5 to the egg's bottom at 96,
     so its centre is 50.5; scaled, that point lands on the tile's centre. */
  const tx = 50 - 50 * ICON_EGG_SCALE;
  const ty = 50 - 50.5 * ICON_EGG_SCALE;
  return (
    `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${HATCH_MARK_VIEWBOX}" width="${size}" height="${size}">` +
    `<defs><linearGradient id="${tileId}" gradientUnits="userSpaceOnUse" x1="0" y1="0" x2="100" y2="100">` +
    `<stop offset="0" stop-color="${from}"/><stop offset="1" stop-color="${to}"/></linearGradient></defs>` +
    `${tile}<g fill="#ffffff" transform="translate(${tx.toFixed(2)} ${ty.toFixed(2)}) scale(${ICON_EGG_SCALE})">` +
    `<path d="${HATCH_MARK_CAP}"/><path d="${HATCH_MARK_BODY}"/></g></svg>\n`
  );
}
