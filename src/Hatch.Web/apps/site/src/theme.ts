/* Day and night for the public site, with no control.
   ---------------------------------------------------------------------------
   With no JavaScript the page follows `prefers-color-scheme` through
   tokens.css alone; neither page carries a `data-theme` attribute. This script
   only honours a stored `hatch.theme` choice, where a script can run.

   The storage key is not inlined here: it is themeStore's, and a second copy of
   it is a second place for the two to drift apart. There is no Auto/Light/Dark
   switch - the stored choice is per-origin, so one made on somebody's own board
   does not reach this site, and the colour-scheme preference governs without it. */
import { applyChoice, readChoice } from '@hatch/ui/themeStore';

applyChoice(readChoice());
