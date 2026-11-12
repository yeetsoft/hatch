/* The parameter vector, its ranges, and how a roll draws one.
   ---------------------------------------------------------------------------
   PARAMETERS is the one table of full ranges (what a person may set by hand)
   and roll windows (where a roll draws from, so it is never unreadable). The
   windows are a first cut, expected to be tuned by eye. */

export interface PaletteParams {
  /** Global rotation of every accent, degrees. */
  hue: number;
  /** Each accent's hue distance from the primary, scaled: below 1 pulls toward it, above 1 pushes apart. */
  spread: number;
  /** Chroma gain on accents. */
  chroma: number;
  /** Lightness nudge on accents, in OKLab lightness. */
  lift: number;
  /** The neutrals' chroma: how coloured the paper is. */
  tint: number;
  /** The paper's hue, degrees from the rotated primary. */
  tintHue: number;
}

export type RollWindow =
  | { lo: number; hi: number }
  /** One of `pick`, plus a draw from -jitter to +jitter, wrapped back into the full range. */
  | { pick: readonly number[]; jitter: number };

export interface ParameterSpec {
  min: number;
  max: number;
  window: RollWindow;
  /** What a control calls it; the table key where absent. */
  label?: string;
  /** Appended to a readout of the value (`°`, `×`); none where absent. */
  unit?: string;
}

/** In draw order: `rollParams` reads this table top to bottom. */
export const PARAMETERS: Readonly<Record<keyof PaletteParams, ParameterSpec>> = {
  hue: { min: 0, max: 360, window: { lo: 0, hi: 360 }, label: 'Hue', unit: '°' },
  spread: { min: 0.5, max: 1.5, window: { lo: 0.8, hi: 1.25 }, label: 'Spread', unit: '×' },
  chroma: { min: 0.5, max: 2, window: { lo: 0.85, hi: 1.6 }, label: 'Chroma', unit: '×' },
  lift: { min: -0.08, max: 0.08, window: { lo: -0.04, hi: 0.04 }, label: 'Lift' },
  tint: { min: 0, max: 0.05, window: { lo: 0.004, hi: 0.03 }, label: 'Tint' },
  tintHue: { min: 0, max: 360, window: { pick: [0, 30, 180, 210], jitter: 12 }, label: 'Tint hue', unit: '°' },
};

/** A roll window as a track draws it: one segment, or more where a picked window wraps past the 0/360 seam. */
export type Band = readonly [from: number, to: number];

export function windowBands(spec: ParameterSpec): Band[] {
  const { min, max, window } = spec;
  if ('lo' in window) return [[window.lo, window.hi]];

  const bands: Band[] = [];
  for (const pick of window.pick) {
    const lo = pick - window.jitter;
    const hi = pick + window.jitter;
    if (lo < min) {
      bands.push([min, hi]);
      bands.push([max + (lo - min), max]);
    } else if (hi > max) {
      bands.push([lo, max]);
      bands.push([min, min + (hi - max)]);
    } else {
      bands.push([lo, hi]);
    }
  }
  return bands;
}

export const PARAMETER_KEYS = Object.keys(PARAMETERS) as (keyof PaletteParams)[];

/** The vector that leaves every accent where it was authored and the paper uncoloured. */
export const IDENTITY: Readonly<PaletteParams> = { hue: 0, spread: 1, chroma: 1, lift: 0, tint: 0, tintHue: 0 };

/**
 * Draws a vector from inside the roll windows. `random` is a function from
 * [0, 1), called exactly once per parameter in table order - twice for a
 * picked one, the pick and then its jitter - so seven times in all.
 */
export function rollParams(random: () => number): PaletteParams {
  const params = { ...IDENTITY };
  for (const key of PARAMETER_KEYS) {
    const { min, max, window } = PARAMETERS[key];
    if ('pick' in window) {
      const picked = window.pick[Math.floor(random() * window.pick.length)];
      const wanted = picked + (random() * 2 - 1) * window.jitter;
      // An angle: a jittered 0 is 348, not -12.
      params[key] = min + ((((wanted - min) % (max - min)) + (max - min)) % (max - min));
    } else {
      params[key] = window.lo + random() * (window.hi - window.lo);
    }
  }
  return params;
}
