/* The palette engine: a seeded OKLCH transform from the house palette to a random one.
   Pure TypeScript with no DOM and no React, so the public site can import it. */
export { derivePalette } from './derive';
export { hexToOklch, oklchToHex, clipChroma, parseCssColor, formatRgba, type Oklch, type Rgba } from './oklch';
export { IDENTITY, PARAMETERS, PARAMETER_KEYS, rollParams, type ParameterSpec, type PaletteParams, type RollWindow } from './params';
export {
  CONTRAST_PAIRS,
  contrastRatio,
  measurePairs,
  pairRatio,
  paintedHex,
  semanticHueGap,
  type ContrastPair,
  type MeasuredPair,
  type Palette,
} from './pairs';
export { ROLES, type Role } from './roles';
export { colourTokens, isColourValue, parseTokenBlocks, type TokenBlock, type TokenBlocks } from './tokenBlocks';
