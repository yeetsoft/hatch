/* What the board's health strip remembers between visits: whether it is shown,
   and which window it reads. Two choices in the browser's own storage,
   namespaced next to `hatch.board.types` (lib/filter.ts), read and written the
   way readBoardTypes and writeBoardTypes are - try/catch around
   `localStorage`, a bad stored value falling back to the default. */

import { DEFAULT_HEALTH_WINDOW, type HealthWindow, parseHealthWindow } from './boardHealth';

/** Where whether the strip is shown is persisted. */
export const STRIP_SHOWN_STORAGE_KEY = 'hatch.board.strip.shown';

/** Where the strip's window is persisted. */
export const STRIP_WINDOW_STORAGE_KEY = 'hatch.board.strip.window';

/** Whether the strip is shown: `true` unless the browser stored `'false'`.
    Anything else, and a store that throws (a partitioned or locked-down
    context), reads as shown - the strip is the board's default. */
export function readStripShown(): boolean {
  try {
    return window.localStorage.getItem(STRIP_SHOWN_STORAGE_KEY) !== 'false';
  } catch {
    /* See above. */
  }
  return true;
}

/** Persists the choice. A store that cannot be written is not worth
    surfacing: the choice holds until the next reload, as writeBoardTypes's
    does. */
export function writeStripShown(shown: boolean): void {
  try {
    window.localStorage.setItem(STRIP_SHOWN_STORAGE_KEY, String(shown));
  } catch {
    /* See above. */
  }
}

/** The stored window, or `8h` for one that is not offered or cannot be read. */
export function readStripWindow(): HealthWindow {
  try {
    return parseHealthWindow(window.localStorage.getItem(STRIP_WINDOW_STORAGE_KEY));
  } catch {
    /* See above. */
  }
  return DEFAULT_HEALTH_WINDOW;
}

/** Persists the choice, swallowing a store that cannot be written. */
export function writeStripWindow(choice: HealthWindow): void {
  try {
    window.localStorage.setItem(STRIP_WINDOW_STORAGE_KEY, choice);
  } catch {
    /* See above. */
  }
}
