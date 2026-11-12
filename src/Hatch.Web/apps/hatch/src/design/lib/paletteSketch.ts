/* The fixed-seed fixture the gallery's palette configurator page draws its
   specimens from: one seed, decoded once, derived into both blocks.
   ---------------------------------------------------------------------------
   No React here - the numbers are cheaper to get right once, outside a
   component. */
import tokensCss from '@hatch/ui/tokens.css?raw';
import {
  decodeSeed,
  derivePalette,
  parseTokenBlocks,
  type Palette,
  type PaletteParams,
} from '@hatch/ui/palette';

/** The one vector the page shows, inside every roll window: hue 200.47,
    spread 1.1, chroma 1.2, lift 0.0097, tint 0.012, tintHue 210.35. A fixture,
    not anybody's theme. */
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
