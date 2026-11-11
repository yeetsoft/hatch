/* Day and night for the public site, with no control.
   ---------------------------------------------------------------------------
   With no JavaScript the page follows `prefers-color-scheme` through
   tokens.css alone; neither page carries a `data-theme` attribute. This script
   only honours a stored `hatch.theme` choice and a stored `hatch.palette` seed, where a
   script can run.

   The storage key is not inlined here: it is themeStore's, and a second copy of
   it is a second place for the two to drift apart. There is no Auto/Light/Dark
   switch - the stored choice is per-origin, so one made on somebody's own board
   does not reach this site, and the colour-scheme preference governs without it. */
import { applyChoice, applyPalette, readChoice, readPalette } from '@hatch/ui/themeStore';

applyPalette(readPalette());
applyChoice(readChoice());
