/* The fixture this design round sketches against: one seed, decoded once,
   derived into both blocks, plus the small readings each panel part needs.
   ---------------------------------------------------------------------------
   No React here - the three layouts on the page are three arrangements of
   the same numbers, and the numbers are cheaper to get right once, outside
   a component. */
import tokensCss from '@hatch/ui/tokens.css?raw';
import {
  decodeSeed,
  derivePalette,
  hexToOklch,
  parseTokenBlocks,
  type Palette,
  type PaletteParams,
} from '@hatch/ui/palette';

/** The one vector this round shows, inside every roll window: hue 200.47,
    spread 1.1, chroma 1.2, lift 0.0097, tint 0.012, tintHue 210.35. A fixture,
    not anybody's theme - the final gallery page keeps this exact string. */
export const SEED = 'hx1-jpl3jz2V';

export interface SketchPalettes {
  params: PaletteParams;
  light: Palette;
  dark: Palette;
}

/** Decodes {@link SEED} and derives both blocks from it. Throws on refusal:
    a seed this file wrote itself refusing to decode is this file's own bug. */
export function sketchPalettes(): SketchPalettes {
  const decoded = decodeSeed(SEED);
  if (!decoded.ok) throw new Error(`paletteSketch: ${decoded.message}`);
  return { params: decoded.params, ...deriveBoth(decoded.params) };
}

const blocks = parseTokenBlocks(tokensCss);

/** Both blocks derived from one vector: what the live configurator re-derives on every move. */
export function deriveBoth(params: PaletteParams): Pick<SketchPalettes, 'light' | 'dark'> {
  return { light: derivePalette(params, blocks.light), dark: derivePalette(params, blocks.dark) };
}

/** The four accents a panel draws around its dial and across its board -
    the families, not every token the engine treats as an accent (the chrome
    gradient and the flow bands are shown elsewhere on the panel). */
export const ACCENT_TOKENS = ['--primary', '--danger', '--success', '--warn'] as const;

/** One accent's position on the dial's rim: its own hue, read off the
    derived hex rather than restated, so the mark can never disagree with the
    swatch next to it. */
export interface RimMark {
  token: string;
  hue: number;
  colour: string;
}

export function rimMarks(palette: Palette): RimMark[] {
  return ACCENT_TOKENS.map((token) => {
    const colour = palette[token];
    return { token, hue: hexToOklch(colour).h, colour };
  });
}
