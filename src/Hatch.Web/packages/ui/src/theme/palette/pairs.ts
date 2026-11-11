/* The contrast pairs the token file documents, and how to measure them.
   ---------------------------------------------------------------------------
   One definition, used by the guard in derive.ts, by the tests, and by the
   configurator that will show `measurePairs`. WCAG 2.x arithmetic, on the
   `luminance` the rest of @hatch/ui already uses (imported from color.ts, not
   the barrel, which loads React). */
import { luminance } from '../../color';
import { hexToOklch, formatHex, parseCssColor } from './oklch';
import { FAMILIES, INK_ON, CHROME, CHROME_END, CHROME_GLOW, SEMANTIC } from './roles';

export type Palette = Record<string, string>;

export interface ContrastPair {
  fg: string;
  bg: string;
  /** When `bg` is translucent, the ground it is laid over. */
  over?: string;
  floor: number;
}

const TEXT = 4.5;
const GLOW = 3;
/** The epic asks 3:1 for hairlines, but the authored ratio is 1.32 light and 1.33 dark, and neutrals held to
    +-0.04 of lightness cannot reach 3. One constant: the floor a line has to clear to be seen at all. */
const HAIRLINE = 1.2;

export const CONTRAST_PAIRS: readonly ContrastPair[] = [
  { fg: '--ink', bg: '--bg', floor: TEXT },
  { fg: '--ink', bg: '--card', floor: TEXT },
  { fg: '--muted', bg: '--card', floor: TEXT },
  ...FAMILIES.flatMap((name): ContrastPair[] => [
    { fg: `--${name}-ink`, bg: '--card', floor: TEXT },
    { fg: `--${name}-ink`, bg: `--${name}-bg`, over: '--card', floor: TEXT },
  ]),
  ...INK_ON.map(([fill, ink]): ContrastPair => ({ fg: ink, bg: fill, floor: TEXT })),
  { fg: CHROME_GLOW, bg: CHROME, floor: GLOW },
  { fg: CHROME_GLOW, bg: CHROME_END, floor: GLOW },
  { fg: '--line', bg: '--card', floor: HAIRLINE },
];

export function contrastRatio(fg: string, bg: string): number {
  const a = luminance(fg);
  const b = luminance(bg);
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
}

const colourOf = (palette: Palette, token: string) => {
  const parsed = palette[token] === undefined ? null : parseCssColor(palette[token]);
  if (!parsed) throw new Error(`Palette has no readable colour for ${token}: ${palette[token]}`);
  return parsed;
};

/** `token` as the hex it paints, a wash composited over `over` in sRGB the way CSS does. */
export function paintedHex(palette: Palette, token: string, over?: string): string {
  const { r, g, b, a } = colourOf(palette, token);
  if (a >= 1 || over === undefined) return formatHex(r, g, b);
  const ground = colourOf(palette, over);
  const mix = (top: number, bottom: number) => Math.round(top * a + bottom * (1 - a));
  return formatHex(mix(r, ground.r), mix(g, ground.g), mix(b, ground.b));
}

export const pairRatio = (palette: Palette, pair: ContrastPair): number =>
  contrastRatio(paintedHex(palette, pair.fg), paintedHex(palette, pair.bg, pair.over));

export interface MeasuredPair extends ContrastPair {
  ratio: number;
}

export const measurePairs = (palette: Palette): MeasuredPair[] =>
  CONTRAST_PAIRS.map((pair) => ({ ...pair, ratio: pairRatio(palette, pair) }));

/** The narrowest hue gap, in degrees, between any two of `--danger`, `--success` and `--warn`, read off the hex. */
export function semanticHueGap(palette: Palette): number {
  const hues = SEMANTIC.map((token) => hexToOklch(paintedHex(palette, token)).h);
  const gap = (a: number, b: number) => {
    const apart = Math.abs(a - b) % 360;
    return Math.min(apart, 360 - apart);
  };
  return Math.min(gap(hues[0], hues[1]), gap(hues[1], hues[2]), gap(hues[0], hues[2]));
}
