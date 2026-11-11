/* The transform: one block of tokens.css and a parameter vector in, a palette out.
   ---------------------------------------------------------------------------
   Pure arithmetic, deterministic: the same params and block give the same
   bytes. The order is the point, because each step reads what the last
   settled:

     1. neutrals          authored lightness, chroma = tint, hue = the rotated primary's + tintHue
     2. accent targets    hue rotated and spread, chroma scaled, lightness lifted, clipped by chroma
     3. semantic hues     danger, success and warn pushed apart, re-measured on the hex
     4. fill guard        each fill carries its authored ink at the floor
     5. derived tokens    washes from the final accents, text forms from the final accents
     6. ink guard         neutral ink, muted and line move a little if the tint cost them contrast

   The `on-*` inks keep the block's authored ink and never flip, so an ink
   cannot cross the midpoint; the fill moves to carry it. Flipping the ink
   instead is a change to `fillGuard` alone. */
import { luminance } from '../../color';
import { CONTRAST_PAIRS, pairRatio, semanticHueGap, type ContrastPair, type Palette } from './pairs';
import { formatRgba, hexToOklch, oklchToHex, parseCssColor, type Oklch } from './oklch';
import type { PaletteParams } from './params';
import {
  ACCENTS,
  CHROME,
  CHROME_END,
  CHROME_GLOW,
  INK_OF,
  INK_ON,
  NEUTRALS,
  SEMANTIC,
  SUCCESS_FILL,
  WASH_OF,
} from './roles';
import { colourTokens, type TokenBlock } from './tokenBlocks';

const MID = 0.5;
/** Kept clear of the midpoint so 8-bit rounding cannot carry a colour across it. */
const SIDE_MARGIN = 0.005;
const STEP = 0.005;
/** A guard stops this far above the floor, so the measured hex clears it with room to spare. */
const SLACK = 0.03;
/** How far a neutral may move in lightness, a little inside the 0.04 the epic allows, for rounding. */
const NEUTRAL_REACH = 0.035;
const MIN_HUE_GAP = 50;
const MAX_HUE_TARGET = 62;

const clamp = (v: number, lo: number, hi: number) => Math.min(hi, Math.max(lo, v));
const wrap360 = (deg: number) => ((deg % 360) + 360) % 360;
/** Into (-180, 180]. */
const wrap180 = (deg: number) => {
  const d = wrap360(deg);
  return d > 180 ? d - 360 : d;
};

/** +1 for a colour authored above the midpoint, -1 below. */
const sideOf = (l: number): 1 | -1 => (l >= MID ? 1 : -1);

/** The lightness range a token may stay in without crossing the midpoint from the side it was authored on. */
const sideRange = (side: 1 | -1): [number, number] => (side > 0 ? [MID + SIDE_MARGIN, 1] : [0, MID - SIDE_MARGIN]);

const required = (block: TokenBlock, token: string): string => {
  const value = block[token];
  if (value === undefined) throw new Error(`The block has no ${token}, which the palette cannot be derived without`);
  return value;
};

/**
 * Steps a colour in lightness toward `limit`, then shrinks its chroma, until
 * every pair in `pairs` clears its floor with the token set to it. Leaves the
 * best it reached in `palette` if nothing does; the sweep tests are what say so.
 */
function settle(
  palette: Palette,
  token: string,
  from: Oklch,
  toward: 1 | -1,
  limit: number,
  pairs: readonly ContrastPair[],
): void {
  let { l, c } = from;
  for (let attempt = 0; attempt < 1000; attempt += 1) {
    palette[token] = oklchToHex(l, c, from.h);
    if (pairs.every((pair) => pairRatio(palette, pair) >= pair.floor + SLACK)) return;
    if (toward * (limit - l) > 1e-9) l = toward > 0 ? Math.min(limit, l + STEP) : Math.max(limit, l - STEP);
    else if (c > 1e-4) c *= 0.9;
    else return;
  }
}

/** The way to move `token` to get away from `ground`: toward the dark if the ground is lighter. */
const awayFrom = (palette: Palette, token: string, ground: string): 1 | -1 =>
  luminance(palette[ground]) > luminance(palette[token]) ? -1 : 1;

/** Where lightness stops when moving `toward` without leaving the authored side of the midpoint. */
const limitOnSide = (toward: 1 | -1, authoredL: number): number => sideRange(sideOf(authoredL))[toward > 0 ? 1 : 0];

const pairsWhere = (test: (pair: ContrastPair) => boolean) => CONTRAST_PAIRS.filter(test);

/** The accent's authored colour carried through the vector. `hue` is the target hue, before any guard. */
function transformed(authored: Oklch, hue: number, params: PaletteParams): Oklch {
  const [lo, hi] = sideRange(sideOf(authored.l));
  return { l: clamp(authored.l + params.lift, lo, hi), c: authored.c * params.chroma, h: wrap360(hue) };
}

/** Pushes the three hues apart, pair by pair in a fixed order, each pair symmetrically, until every gap reaches `target`. */
function relax(hues: number[], target: number): number[] {
  const out = [...hues];
  const pairs = [
    [0, 1],
    [1, 2],
    [0, 2],
  ];
  for (let pass = 0; pass < 200; pass += 1) {
    let moved = false;
    for (const [i, j] of pairs) {
      const signed = wrap180(out[j] - out[i]);
      const gap = Math.abs(signed);
      if (gap >= target - 1e-6) continue;
      const push = (target - gap) / 2;
      const away = signed >= 0 ? 1 : -1;
      out[i] -= away * push;
      out[j] += away * push;
      moved = true;
    }
    if (!moved) break;
  }
  return out.map(wrap360);
}

export function derivePalette(params: PaletteParams, block: TokenBlock): Palette {
  const palette: Palette = {};
  const tokens = colourTokens(block);
  for (const token of tokens) palette[token] = block[token];

  const has = (token: string) => token in palette;
  const authored = (token: string) => hexToOklch(required(block, token));
  const primary = authored('--primary');
  const rotatedPrimary = primary.h + params.hue;

  // 1. Neutrals: the paper takes a faint colour.
  for (const token of NEUTRALS.filter(has)) {
    palette[token] = oklchToHex(authored(token).l, params.tint, rotatedPrimary + params.tintHue);
  }

  // 2 to 4. Accents, with the semantic hues re-measured on the hex until they clear.
  const accentHue = (token: string) => rotatedPrimary + wrap180(authored(token).h - primary.h) * params.spread;
  const accentTokens = ACCENTS.filter(has);
  const fills = INK_ON.filter(([fill, ink]) => has(fill) && has(ink));

  let accents: Palette = {};
  let targets: Record<string, Oklch> = {};
  for (let target = MIN_HUE_GAP; target <= MAX_HUE_TARGET; target += 1) {
    const hues: Record<string, number> = Object.fromEntries(accentTokens.map((token) => [token, accentHue(token)]));
    const relaxed = relax(SEMANTIC.map((token) => hues[token]), target);
    SEMANTIC.forEach((token, at) => {
      hues[token] = relaxed[at];
    });
    if (has(SUCCESS_FILL)) {
      hues[SUCCESS_FILL] = hues['--success'] + wrap180(authored(SUCCESS_FILL).h - authored('--success').h);
    }

    accents = { ...palette };
    targets = {};
    for (const token of Object.keys(hues)) {
      targets[token] = transformed(authored(token), hues[token], params);
      accents[token] = oklchToHex(targets[token].l, targets[token].c, targets[token].h);
    }
    for (const [fill, ink] of fills) {
      const toward = awayFrom(accents, fill, ink);
      settle(
        accents,
        fill,
        targets[fill],
        toward,
        limitOnSide(toward, authored(fill).l),
        pairsWhere((pair) => pair.bg === fill && pair.fg === ink),
      );
    }
    if (semanticHueGap(accents) >= MIN_HUE_GAP) break;
  }
  Object.assign(palette, accents);

  // 5. Derived: washes from the final accents; text forms from the final accents too.
  for (const [wash, base] of Object.entries(WASH_OF).filter(([wash]) => has(wash))) {
    const alpha = parseCssColor(required(block, wash))!.a;
    const { r, g, b } = parseCssColor(palette[base])!;
    palette[wash] = formatRgba(r, g, b, alpha);
  }
  for (const [ink, base] of Object.entries(INK_OF).filter(([ink]) => has(ink))) {
    const was = authored(ink);
    const accent = hexToOklch(palette[base]);
    const c = Math.max(0, accent.c + (was.c - authored(base).c));
    const toward = awayFrom(palette, ink, '--card');
    settle(
      palette,
      ink,
      { l: was.l, c, h: accent.h },
      toward,
      limitOnSide(toward, was.l),
      pairsWhere((pair) => pair.fg === ink),
    );
  }

  // The glow sits against the bar, away from it.
  if (has(CHROME_GLOW)) {
    const was = authored(CHROME_GLOW);
    const bars = luminance(palette[CHROME]) + luminance(palette[CHROME_END]);
    const toward = luminance(palette[CHROME_GLOW]) * 2 > bars ? 1 : -1;
    settle(palette, CHROME_GLOW, targets[CHROME_GLOW], toward, limitOnSide(toward, was.l), pairsWhere((pair) => pair.fg === CHROME_GLOW));
  }

  // 6. Neutral inks: the tint may have cost them a little contrast. Lightness first, then colour out.
  for (const token of ['--ink', '--muted', '--line'].filter(has)) {
    const was = authored(token);
    const toward = awayFrom(palette, token, '--card');
    const reach = clamp(was.l + toward * NEUTRAL_REACH, ...sideRange(sideOf(was.l)));
    settle(
      palette,
      token,
      { l: was.l, c: params.tint, h: rotatedPrimary + params.tintHue },
      toward,
      reach,
      pairsWhere((pair) => pair.fg === token),
    );
  }

  // The output is the block's own tokens, in its own order.
  return Object.fromEntries(tokens.map((token) => [token, palette[token]]));
}
