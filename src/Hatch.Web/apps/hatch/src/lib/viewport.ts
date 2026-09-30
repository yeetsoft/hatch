/* The one width breakpoint the app owns. A media query cannot read a custom
   property, so this literal is the licensed exception to "a spacing value
   written here as a literal is a bug" - see docs/design-system-architecture.md,
   "The phone". CSS restates it verbatim in the same words; this is the value a
   component reads. */

import { useEffect, useState } from 'react';

export const PHONE_QUERY = '(max-width: 40rem)';

/**
 * Whether the viewport matches {@link PHONE_QUERY} right now.
 *
 * `window` itself is absent in this workspace's tests (plain Node, no jsdom),
 * not just `matchMedia` - the existence check has to come first or a call from
 * viewport.test.ts throws.
 */
export function matchesPhone(): boolean {
  return typeof window !== 'undefined'
    && typeof window.matchMedia === 'function'
    && window.matchMedia(PHONE_QUERY).matches;
}

/** Calls back when the match flips. Returns the unsubscribe. */
function watchPhone(onChange: (isPhone: boolean) => void): () => void {
  const query = window.matchMedia(PHONE_QUERY);
  const listener = (event: MediaQueryListEvent) => onChange(event.matches);
  query.addEventListener('change', listener);
  return () => query.removeEventListener('change', listener);
}

/** Whether the viewport is phone-width, re-answered as it changes. */
export function usePhone(): boolean {
  const [isPhone, setIsPhone] = useState<boolean>(matchesPhone);

  useEffect(() => {
    const unwatch = watchPhone(setIsPhone);
    /* The viewport may have changed between the initial render and this effect. */
    setIsPhone(matchesPhone());
    return unwatch;
  }, []);

  return isPhone;
}

/** How far the keyboard has pushed the visual viewport's bottom edge above
    the layout viewport's, in px. 0 wherever `visualViewport` is unsupported -
    there a `position: fixed; bottom: 0` row already sits at the true bottom,
    which is AC3's fallback. */
export function useKeyboardInset(): number {
  const [inset, setInset] = useState(0);

  useEffect(() => {
    const vv = window.visualViewport;
    if (!vv) return;
    const update = () => setInset(Math.max(0, window.innerHeight - vv.height - vv.offsetTop));
    update();
    vv.addEventListener('resize', update);
    vv.addEventListener('scroll', update);
    return () => {
      vv.removeEventListener('resize', update);
      vv.removeEventListener('scroll', update);
    };
  }, []);

  return inset;
}
