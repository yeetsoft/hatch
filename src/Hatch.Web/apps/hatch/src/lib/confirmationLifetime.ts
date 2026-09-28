/**
 * How long a chicklet stays in the corner, as the operator chose it.
 *
 * Remembered by the browser rather than by the install, the way the theme is
 * (packages/ui/src/theme/themeStore.ts): how long somebody needs to read the
 * corner is a fact about them and their screen, and an install-wide value would
 * let one person set everybody's reading time. It needs no request, so it is
 * there even when the rest of the Settings page failed to load.
 *
 * No React in here, so the provider reads it afresh every time it raises a
 * chicklet - which is all "takes effect as soon as it is made" needs.
 */

/** Where the choice is persisted. Namespaced - apps share an origin. */
export const LIFETIME_STORAGE_KEY = 'hatch.confirmations.lifetime';

/** The stored word for "stay until closed". */
const NEVER = 'never';

/** Fifteen seconds: long enough to read a sentence and reach for Undo. */
export const DEFAULT_LIFETIME = 15_000;

/** A chicklet's life in milliseconds, or null for one that never leaves. */
export type Lifetime = number | null;

export interface LifetimeChoice {
  value: Lifetime;
  label: string;
}

/** What the Settings page offers, shortest first. */
export const LIFETIME_CHOICES: LifetimeChoice[] = [
  { value: 10_000, label: '10 seconds' },
  { value: 15_000, label: '15 seconds' },
  { value: 30_000, label: '30 seconds' },
  { value: 60_000, label: '1 minute' },
  { value: null, label: 'Never' },
];

/**
 * The stored choice, or the default.
 *
 * localStorage *throws* in a partitioned or locked-down context, and the corner
 * still has to work, so an unreadable store means the default. So does a value
 * this build does not offer.
 */
export function readLifetime(): Lifetime {
  try {
    const stored = window.localStorage.getItem(LIFETIME_STORAGE_KEY);
    if (stored === NEVER) return null;
    const ms = stored === null ? NaN : Number(stored);
    return LIFETIME_CHOICES.some((c) => c.value === ms) ? ms : DEFAULT_LIFETIME;
  } catch {
    return DEFAULT_LIFETIME;
  }
}

/** Persists the choice. A store that cannot be written is not worth surfacing:
    the choice holds until the next reload, as the theme's does. */
export function writeLifetime(value: Lifetime): void {
  try {
    window.localStorage.setItem(LIFETIME_STORAGE_KEY, value === null ? NEVER : String(value));
  } catch {
    /* See above. */
  }
}
