import { useEffect, useRef, useState } from 'react';
import { getHatchRevision } from '../api/client';
import type { Revision } from '../types';

/**
 * Reads `/api/hatch-revision` once, the first time `open` turns true, and
 * never again - no interval, no `visibilitychange`, no refetch on focus. The
 * gear's panel is the only caller, and a build stamp that moves under the
 * reader while the panel is open is a different feature than the one HA-207
 * asked for.
 *
 * Silent on failure, the rule `lib/useMe.tsx` and `lib/useUtilization.ts` both
 * state in their own headers: a failed read leaves the answer null, which
 * `BuildStamp` already treats the same as "not asked yet".
 */
export function useRevision(open: boolean): Revision | null {
  const [revision, setRevision] = useState<Revision | null>(null);
  const asked = useRef(false);

  useEffect(() => {
    if (!open || asked.current) return;
    asked.current = true;

    (async () => {
      try {
        setRevision(await getHatchRevision());
      } catch {
        // Deliberately silent - see the note above.
      }
    })();
  }, [open]);

  return revision;
}
