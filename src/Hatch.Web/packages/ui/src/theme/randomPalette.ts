/* What Random needs beyond the store, with no React and no DOM.
   ---------------------------------------------------------------------------
   Two callers, <ThemeSwitch> and standalone/topbar.ts, drive the same control
   and must not drift: rolling, checking a pasted seed, and the two stored keys
   live here once.

   `hatch.palette` is the *applied* seed (themeStore.ts) - absent is the house
   palette, and the site and every other reader look at that key alone. Random
   is therefore on exactly when it is set. `hatch.palette.last` is the seed most
   recently rolled, pasted or applied, which turning Random off leaves alone, so
   turning it back on returns the same theme. */
import { PALETTE_STORAGE_KEY, writePalette } from './themeStore';
import { rollParams } from './palette/params';
import { decodeSeed, encodeSeed } from './palette/seed';

/** Where the last seed is kept while Random is off. */
export const LAST_PALETTE_STORAGE_KEY = `${PALETTE_STORAGE_KEY}.last`;

/** The last seed Random held, or `null`. Decode-checked, as `readPalette` is. */
export function readLastSeed(): string | null {
  try {
    const stored = window.localStorage.getItem(LAST_PALETTE_STORAGE_KEY)?.trim();
    return stored && decodeSeed(stored).ok ? stored : null;
  } catch {
    return null;
  }
}

/**
 * Applies a seed in storage - or the house palette for `null`. A seed is also
 * remembered as the last one; `null` leaves that memory alone. Throws are
 * swallowed, as in `writeChoice`.
 */
export function storePalette(seed: string | null): void {
  writePalette(seed);
  if (seed === null) return;
  try {
    window.localStorage.setItem(LAST_PALETTE_STORAGE_KEY, seed);
  } catch {
    /* See writeChoice. */
  }
}

/** [0, 1) from `crypto.getRandomValues`, or `Math.random` where there is no `crypto`. */
export function cryptoRandom(): number {
  if (typeof crypto === 'undefined' || typeof crypto.getRandomValues !== 'function') return Math.random();
  return crypto.getRandomValues(new Uint32Array(1))[0] / 2 ** 32;
}

/** A new seed drawn from the engine's roll windows. The seed is what is kept, not the source. */
export function rollSeed(random: () => number = cryptoRandom): string {
  return encodeSeed(rollParams(random));
}

export type SeedCheck = { ok: true; seed: string } | { ok: false; message: string };

/** A typed or pasted seed, trimmed, or the engine's own words for why not. */
export function checkSeed(input: string): SeedCheck {
  const decoded = decodeSeed(input);
  return decoded.ok ? { ok: true, seed: input.trim() } : { ok: false, message: decoded.message };
}

/** The seed Random-on should apply: the last one, or a fresh roll when there is none. */
export function seedForRandomOn(): string {
  return readLastSeed() ?? rollSeed();
}
