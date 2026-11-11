/* The theme mechanism, with no React in it.
   ---------------------------------------------------------------------------
   `auto | light | dark`, persisted, resolved, and written to `<html
   data-theme>` - which is what tokens.css keys its dark palette on.

   This file is framework-free on purpose. <ThemeProvider> is one caller; the
   others are standalone/topbar.ts, the bar that renders on pages Hatch did not
   build with Vite and React (Swagger UI, the logo export), and the public
   site's theme.ts, which applies the stored choice and has no control. Those
   callers must read and write the *same* stored choice as the apps - a house
   where the theme you picked in admin does not survive the click into Swagger
   is a house with two themes - and importing ThemeProvider to get it would drag React
   onto a static page to run twelve lines of localStorage.

   So the mechanism lives here and both callers use it. The React file below
   holds only what is genuinely React: state, an effect, a context. */
import tokensCss from '../tokens.css?raw';
import { derivePalette } from './palette/derive';
import { decodeSeed } from './palette/seed';
import { paletteCss } from './palette/sheet';
import type { Palette } from './palette/pairs';
import { parseTokenBlocks } from './palette/tokenBlocks';

/** What the operator chose. `auto` defers to the OS preference. */
export type ThemeChoice = 'auto' | 'light' | 'dark';

/** What that choice resolves to right now. `auto` collapses to one of these. */
export type ResolvedTheme = 'light' | 'dark';

/** Where the choice is persisted. Namespaced - apps share an origin. */
export const THEME_STORAGE_KEY = 'hatch.theme';

/** Where the palette seed is persisted: the `hx1-` string, beside the choice. */
export const PALETTE_STORAGE_KEY = 'hatch.palette';

export const DARK_QUERY = '(prefers-color-scheme: dark)';

export function isThemeChoice(value: unknown): value is ThemeChoice {
  return value === 'auto' || value === 'light' || value === 'dark';
}

/**
 * The stored choice, or `auto`.
 *
 * localStorage *throws* rather than returning null in a partitioned or
 * locked-down context (Safari private browsing, a third-party frame). The page
 * still has to render, so an unreadable store means `auto`.
 */
export function readChoice(): ThemeChoice {
  try {
    const stored = window.localStorage.getItem(THEME_STORAGE_KEY);
    return isThemeChoice(stored) ? stored : 'auto';
  } catch {
    return 'auto';
  }
}

/**
 * Persists the choice. A store that cannot be written is not an error worth
 * surfacing: the choice holds for this tab and is forgotten on reload, which is
 * better than refusing to change theme at all.
 */
export function writeChoice(choice: ThemeChoice): void {
  try {
    window.localStorage.setItem(THEME_STORAGE_KEY, choice);
  } catch {
    /* See above. */
  }
}

/**
 * The stored palette seed, or `null` for the house palette.
 *
 * Only a seed the engine accepts reads back: an unreadable store, a malformed
 * value and a seed an older engine wrote all mean no palette, so a stale value
 * can never half-apply.
 */
export function readPalette(): string | null {
  try {
    const stored = window.localStorage.getItem(PALETTE_STORAGE_KEY)?.trim();
    return stored && decodeSeed(stored).ok ? stored : null;
  } catch {
    return null;
  }
}

/** Persists the seed, or forgets it for `null`. Throws are swallowed, as in `writeChoice`. */
export function writePalette(seed: string | null): void {
  try {
    if (seed === null) window.localStorage.removeItem(PALETTE_STORAGE_KEY);
    else window.localStorage.setItem(PALETTE_STORAGE_KEY, seed);
  } catch {
    /* See writeChoice. */
  }
}

/**
 * `window` itself is absent under `renderToStaticMarkup` in a test - there is
 * no partitioned-storage equivalent for a media query, so the guard is a
 * plain existence check rather than the try/catch `readChoice` uses. No
 * preference to read means light, the same default an unset choice resolves
 * to server-side.
 */
export function prefersDark(): boolean {
  return typeof window !== 'undefined' && window.matchMedia(DARK_QUERY).matches;
}

/**
 * Writes the choice to `<html data-theme>`.
 *
 * `auto` *removes* the attribute rather than writing the resolved value: with
 * no attribute the `prefers-color-scheme` guard in tokens.css governs, so an OS
 * that changes theme while the tab is backgrounded is already correct on the
 * next paint, with no JavaScript involved.
 */
export function applyChoice(choice: ThemeChoice): void {
  const root = document.documentElement;
  if (choice === 'auto') {
    root.removeAttribute('data-theme');
  } else {
    root.setAttribute('data-theme', choice);
  }
  if (choice !== 'auto') applyThemeColor(choice);
  /* Auto hands the status bar back to index.html's OS-keyed pair - unless a
     palette is on, since that pair is the house chrome, not the derived one. */
  else applyThemeColor(derived ? (prefersDark() ? 'dark' : 'light') : null);
}

// --chrome, read off tokens.css rather than restated as a third literal
// pair. The dark value comes from the `[data-theme='dark']` block (not the
// `prefers-color-scheme` media block) because that is the selector active
// when applyChoice sets the attribute above - the two blocks are identical,
// tokens.test.ts already enforces this.
function chromeIn(block: Record<string, string>, name: string): string {
  const value = block['--chrome'];
  if (!value) throw new Error(`tokens.css: no --chrome in the ${name} block`);
  return value;
}
const BLOCKS = parseTokenBlocks(tokensCss);
const CHROME: Record<ResolvedTheme, string> = {
  light: chromeIn(BLOCKS.light, 'light'),
  dark: chromeIn(BLOCKS.dark, 'dark'),
};

/** The two palettes the applied seed derived, or null for the house palette. */
let derived: Record<ResolvedTheme, Palette> | null = null;

/** The colour the status bar should carry when Hatch has resolved to `resolved`. */
export function themeColorFor(resolved: ResolvedTheme): string {
  return derived?.[resolved]['--chrome'] ?? CHROME[resolved];
}

const OVERRIDE_ATTR = 'data-theme-color-override';

/** Writes or removes the one theme-color meta this file owns. `null` removes
 * it, handing the OS-keyed pair in index.html back control - the same
 * handoff `applyChoice`'s own `data-theme` branch makes for `auto`. */
export function applyThemeColor(resolved: ResolvedTheme | null): void {
  if (typeof document === 'undefined') return;
  const existing = document.head.querySelector<HTMLMetaElement>(`meta[${OVERRIDE_ATTR}]`);
  if (resolved === null) {
    existing?.remove();
    return;
  }
  const meta = existing ?? document.createElement('meta');
  if (!existing) {
    meta.setAttribute('name', 'theme-color');
    meta.setAttribute(OVERRIDE_ATTR, '');
    document.head.prepend(meta);
  }
  meta.setAttribute('content', themeColorFor(resolved));
}

const PALETTE_ATTR = 'data-theme-palette';

/**
 * Puts a seeded palette on the page, or takes it off with `null`.
 *
 * One `<style>` this file owns, appended to <head> so it comes after the
 * token stylesheet and wins on order alone. It restates tokens.css's three
 * colour blocks (see palette/sheet.ts), so Auto still flips with the OS in CSS
 * with no JavaScript. It is not inline style on <html>: an inline `style`
 * outranks every selector, so it could hold neither a light and a dark value
 * under the media guard nor under the attribute.
 *
 * A seed the engine refuses removes the sheet, the same as none.
 */
export function applyPalette(seed: string | null): void {
  if (typeof document === 'undefined') return;
  const existing = document.head.querySelector<HTMLStyleElement>(`style[${PALETTE_ATTR}]`);
  const decoded = seed === null ? null : decodeSeed(seed);
  if (!decoded?.ok) {
    derived = null;
    existing?.remove();
  } else {
    derived = {
      light: derivePalette(decoded.params, BLOCKS.light),
      dark: derivePalette(decoded.params, BLOCKS.dark),
    };
    const style = existing ?? document.createElement('style');
    if (!existing) {
      style.setAttribute(PALETTE_ATTR, '');
      document.head.appendChild(style);
    }
    style.textContent = paletteCss(decoded.params, BLOCKS);
  }
  /* The status bar follows whichever choice is on <html> now. */
  const attr = document.documentElement.getAttribute('data-theme');
  if (attr === 'light' || attr === 'dark') applyThemeColor(attr);
  else applyChoice('auto');
}

/** Calls back when the OS preference flips. Returns the unsubscribe. */
export function watchSystemTheme(onChange: (dark: boolean) => void): () => void {
  const query = window.matchMedia(DARK_QUERY);
  const listener = (event: MediaQueryListEvent) => onChange(event.matches);
  query.addEventListener('change', listener);
  return () => query.removeEventListener('change', listener);
}
