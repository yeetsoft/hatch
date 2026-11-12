/* A theme seed shared as a link.
   ---------------------------------------------------------------------------
   `?palette=<seed>` is the whole contract: a link carrying it applies that
   seed on arrival, the same way pasting the seed into the picker does, and
   is then cleaned off the address so it is not re-applied on the next
   navigation and does not end up in a bookmark by accident.

   Consumed once, at boot, before React and before the router mount - never
   inside <ThemeProvider>'s state initialisers, where a StrictMode initialiser
   runs twice. Storing is all "turns Random on" needs: every caller already
   runs `applyPalette(readPalette())` right after, and <ThemeProvider> reads
   the store on mount. */
import { checkSeed, storePalette } from './randomPalette';

export const SEED_PARAM = 'palette';

/**
 * `seed` is the first `palette` value in `href`, or `null` with none.
 * `cleaned` is `href`'s path, search and hash with every `palette` param
 * removed - other params and the hash survive, and no `?` is left dangling
 * with none left.
 */
export function seedFromHref(href: string): { seed: string | null; cleaned: string } {
  const url = new URL(href);
  const seed = url.searchParams.get(SEED_PARAM);
  url.searchParams.delete(SEED_PARAM);
  const search = url.searchParams.toString();
  return { seed, cleaned: url.pathname + (search ? `?${search}` : '') + url.hash };
}

/**
 * Applies a seed carried in `win`'s address, or does nothing with none.
 *
 * A seed the engine refuses is said in the console, and nothing is stored -
 * the page renders in whatever theme was already stored. Either way the
 * parameter is stripped from the address bar with `replaceState`, which
 * never adds a history entry and - passed the existing `history.state` -
 * keeps whatever key react-router already put there. A sandboxed frame
 * throws on history access, so that call is guarded.
 */
export function applySharedSeed(win: Window = window): void {
  const { seed, cleaned } = seedFromHref(win.location.href);
  if (seed === null) return;

  const checked = checkSeed(seed);
  if (checked.ok) storePalette(checked.seed);
  else console.warn(`Ignoring ?${SEED_PARAM}= - ${checked.message}`);

  try {
    win.history.replaceState(win.history.state, '', cleaned);
  } catch {
    /* A sandboxed frame throws on history access. */
  }
}

/** `href` (the current page's address by default) with `palette=<seed>` set, replacing any existing value and keeping everything else. */
export function shareLink(seed: string, href: string = window.location.href): string {
  const url = new URL(href);
  url.searchParams.delete(SEED_PARAM);
  url.searchParams.append(SEED_PARAM, seed);
  return url.toString();
}
