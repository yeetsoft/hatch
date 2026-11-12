import { useCallback, useEffect, useLayoutEffect, useMemo, useState, type ReactNode } from 'react';
import { ThemeContext, type ThemeContextValue } from './themeContext';
import {
  applyChoice,
  applyPalette,
  prefersDark,
  readChoice,
  readPalette,
  watchSystemTheme,
  writeChoice,
  type ResolvedTheme,
  type ThemeChoice,
} from './themeStore';
import { readLastSeed, storePalette } from './randomPalette';

/**
 * Resolves `auto | light | dark` and writes it to `<html data-theme>`, which is
 * what @hatch/ui's tokens.css keys its dark palette on.
 *
 * The reading, writing and resolving are themeStore.ts - shared verbatim with
 * the standalone bar, so a theme chosen in admin is the theme Swagger opens
 * with. What is here is the React around it: the state the components read,
 * the subscription, and the context.
 */
export function ThemeProvider({ children }: { children: ReactNode }) {
  const [choice, setStoredChoice] = useState<ThemeChoice>(readChoice);
  const [palette, setStoredPalette] = useState<string | null>(readPalette);
  const [lastSeed, setLastSeed] = useState<string | null>(readLastSeed);
  const [systemDark, setSystemDark] = useState<boolean>(prefersDark);

  useEffect(() => {
    const unwatch = watchSystemTheme(setSystemDark);
    /* The OS may have flipped between the initial state and this effect. */
    setSystemDark(prefersDark());
    return unwatch;
  }, []);

  const resolved: ResolvedTheme = choice === 'auto' ? (systemDark ? 'dark' : 'light') : choice;

  /* Layout, not passive: this runs before the browser paints, so a stored
     `dark` on a light-preference machine never shows a light frame first, and
     a stored seed never shows the house palette first. The palette goes on
     before the choice so the status bar is written from the derived chrome;
     `resolved` is a dependency so Auto's status bar follows an OS flip. */
  useLayoutEffect(() => {
    applyPalette(palette);
    applyChoice(choice);
  }, [choice, palette, resolved]);

  const setChoice = useCallback((next: ThemeChoice) => {
    setStoredChoice(next);
    writeChoice(next);
  }, []);

  const setPalette = useCallback((next: string | null) => {
    setStoredPalette(next);
    if (next !== null) setLastSeed(next);
    storePalette(next);
  }, []);

  /* State only - no storage, no last seed. The layout effect above puts it on
     the document and every reader of `palette` follows, so a candidate shows on
     the real page and a reload still finds what was stored. */
  const previewPalette = useCallback((next: string | null) => {
    setStoredPalette(next);
  }, []);

  const value = useMemo<ThemeContextValue>(
    () => ({ choice, resolved, setChoice, palette, lastSeed, setPalette, previewPalette }),
    [choice, resolved, setChoice, palette, lastSeed, setPalette, previewPalette],
  );

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}
