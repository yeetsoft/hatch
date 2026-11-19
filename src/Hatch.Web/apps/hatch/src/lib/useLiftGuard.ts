import { useEffect } from 'react';

/** The class the document wears while a card is lifted; App.css hangs the
    overscroll, text-selection and link-callout rules on it. */
export const LIFTING_CLASS = 'hatch-lifting';

/** While `active`, keeps the page still under a lifted card: no scroll
    chain, no text selection, no long-press menu. dnd-kit's own sensor stops
    the touchmove it listens to, but iOS Safari also opens its link menu and
    selection a beat after the lift, and takes the touch with it, so the
    card stops following the finger. These listeners sit on the window in the
    capture phase, ahead of everything else - the pull-to-refresh listener on
    the document included, which then sees a touchmove already prevented and
    stands down. Cleaning up on `active` going false, or on unmount, leaves
    the page exactly as it was. */
export function useLiftGuard(active: boolean): void {
  useEffect(() => {
    if (!active) return;

    const root = document.documentElement;
    const stop = (event: Event) => {
      if (event.cancelable) event.preventDefault();
    };
    const options: AddEventListenerOptions = { passive: false, capture: true };

    root.classList.add(LIFTING_CLASS);
    window.addEventListener('touchmove', stop, options);
    window.addEventListener('selectstart', stop, options);
    window.addEventListener('contextmenu', stop, options);
    return () => {
      root.classList.remove(LIFTING_CLASS);
      window.removeEventListener('touchmove', stop, options);
      window.removeEventListener('selectstart', stop, options);
      window.removeEventListener('contextmenu', stop, options);
    };
  }, [active]);
}
