/* A status column's colour, handed to the stylesheet as custom properties.
   The arithmetic that reads a colour's luminance and picks the ink to write
   on it is shared with a project's own mark now, and lives in `@hatch/ui`
   (packages/ui/src/color.ts); this file keeps only what is specific to a
   status - the default grey an uncoloured column draws, and the pairing
   that hands it to App.css. */
import { contrastInk, safeColor } from '@hatch/ui';

/** What a colour this cannot read falls back to - the same grey the API defaults a column to. */
export const DEFAULT_STATUS_COLOR = '#6b7280';

/**
 * The custom properties every status-coloured element is drawn from. Handed to
 * an element's `style`, read by App.css - so one component decides the colour
 * and the stylesheet decides what to do with it, rather than each site
 * inventing its own inline paint.
 */
export function statusVars(color: string | null | undefined): Record<string, string> {
  const safe = safeColor(color);
  return { '--status-color': safe, '--status-ink': contrastInk(safe) };
}
